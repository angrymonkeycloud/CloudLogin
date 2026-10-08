using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Mints and validates CloudLogin tokens.
/// <para>
/// This type is the single place a user identity becomes a bearer credential.
/// Nothing else in the system may turn a user id into proof of identity, which is
/// what makes "the caller told us who they are" impossible to express downstream.
/// </para>
/// </summary>
public sealed class CloudLoginTokenService(
    CloudLoginSigningKeyManager keyManager,
    ICloudLoginTokenStore store,
    IOptions<CloudLoginTokenOptions> options,
    ILogger<CloudLoginTokenService> logger,
    IClientDirectory? clients = null,
    IAuditLogger? audit = null)
{
    private readonly CloudLoginSigningKeyManager _keyManager = keyManager;
    private readonly ICloudLoginTokenStore _store = store;
    private readonly CloudLoginTokenOptions _options = options.Value;
    private readonly ILogger<CloudLoginTokenService> _logger = logger;
    private readonly JsonWebTokenHandler _handler = new();

    // Without a registry the directory still answers for configuration clients, so the token
    // endpoints have one code path whether or not applications are managed through the admin.
    private readonly IClientDirectory _clients = clients ?? new ClientDirectory(options);

    /// <summary>The lifetimes of the tokens the authority issues.</summary>
    private readonly record struct TokenPolicy(TimeSpan AccessLifetime, TimeSpan RefreshLifetime);

    private TokenPolicy DefaultPolicy => new(_options.AccessTokenLifetime, _options.RefreshTokenLifetime);

    /// <summary>
    /// Issues a fresh access/refresh pair for an interactively authenticated user.
    /// The caller must already have proven the user's identity &mdash; this method
    /// does not authenticate, it only attests.
    /// </summary>
    public async Task<CloudLoginTokenResponse> IssueAsync(
        CloudUser user,
        string audience,
        string? sessionId = null,
        bool includeRefreshToken = true,
        string? clientIp = null,
        string? userAgent = null,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == Guid.Empty)
            throw new InvalidOperationException("Cannot issue a token for a user without an identifier.");

        if (user.IsLocked)
            throw new InvalidOperationException("Cannot issue a token for a locked user.");

        EnsureAudience(audience);

        // A blocked application gets nothing, whether it is the audience or the one asking.
        if (!await _clients.IsAllowedAsync(audience, cancellationToken) || (clientId is not null && clientId != audience && !await _clients.IsAllowedAsync(clientId, cancellationToken)))
            throw new InvalidOperationException($"The application '{clientId ?? audience}' is blocked or cannot be checked right now.");

        TokenPolicy policy = DefaultPolicy;

        sessionId ??= NewOpaqueToken(16);

        string accessToken = await CreateAccessTokenAsync(
            user,
            audience,
            scope: null,
            sessionId,
            actor: null,
            policy,
            cancellationToken);

        string? refreshToken = null;

        // A refresh token must name the client that will present it. With none (a cookie-session issuance), no refresh token is
        // issued unless the explicit compatibility switch says so, so no refresh path exists without client authentication.
        if (includeRefreshToken && (!string.IsNullOrEmpty(clientId) || _options.AllowUnboundRefreshTokens))
            refreshToken = await CreateRefreshTokenAsync(
                user.Id,
                familyId: NewOpaqueToken(16),
                sessionId,
                audience,
                scope: null,
                clientIp,
                userAgent,
                policy,
                cancellationToken,
                clientId);

        await AuditIssuedAsync(user.Id, clientId ?? audience, audience, cancellationToken);

        return BuildResponse(accessToken, refreshToken, scope: null, user, policy);
    }

    /// <summary>
    /// Exchanges a rotating refresh token for a new pair.
    /// <para>
    /// Presenting an already-consumed token means the same credential is in two
    /// places, which is theft until proven otherwise. The entire rotation family is
    /// revoked rather than failing just this call: the attacker is locked out, and
    /// the legitimate client is forced through a fresh sign-in.
    /// </para>
    /// </summary>
    public async Task<CloudLoginTokenResponse?> RefreshAsync(
        string refreshToken,
        Func<Guid, CancellationToken, Task<CloudUser?>> userLookup,
        string? clientIp = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default,
        ClientIdentity? client = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        string hash = HashToken(refreshToken);
        CloudLoginRefreshToken? stored = await _store.FindRefreshTokenAsync(hash, cancellationToken);

        if (stored is null)
            return null;

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (stored.ConsumedOn is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}, family {FamilyId}. Revoking the family.",
                stored.UserId,
                stored.FamilyId);

            await _store.RevokeFamilyAsync(stored.FamilyId, cancellationToken);
            return null;
        }

        if (!stored.IsActive(now))
            return null;

        if (string.IsNullOrEmpty(stored.ClientId) && !_options.AllowUnboundRefreshTokens)
        {
            _logger.LogWarning("A refresh token that names no client was refused. It predates client binding; the person signs in again.");
            return null;
        }

        if (!string.IsNullOrEmpty(stored.ClientId) && !string.Equals(stored.ClientId, client?.ClientId, StringComparison.Ordinal))
        {
            _logger.LogWarning("A refresh token issued to client {ClientId} was presented by {Presenter}.", stored.ClientId, client?.ClientId ?? "an unauthenticated caller");
            return null;
        }

        CloudUser? user = await userLookup(stored.UserId, cancellationToken);

        if (user is null || user.IsLocked || user.Id == Guid.Empty)
            return null;

        string audience = stored.Audience ?? stored.ClientId ?? string.Empty;

        // A blocked application cannot refresh. Its family is kept, so unblocking it resumes service.
        if (string.IsNullOrEmpty(audience) || !await _clients.IsAllowedAsync(stored.ClientId ?? audience, cancellationToken))
            return null;

        TokenPolicy policy = DefaultPolicy;
        string rotated;

        if (_store is IAtomicCloudLoginTokenStore atomicStore)
        {
            (rotated, CloudLoginRefreshToken replacement) = CreateRefreshTokenRecord(
                user.Id, stored.FamilyId, stored.SessionId, audience, stored.Scope, clientIp, userAgent, policy, stored.ClientId);

            CloudLoginRefreshRotationResult result = await atomicStore.RotateRefreshTokenAsync(
                stored, replacement, cancellationToken);

            if (result != CloudLoginRefreshRotationResult.Succeeded)
            {
                if (result == CloudLoginRefreshRotationResult.ReuseDetected)
                    _logger.LogWarning(
                        "Refresh token reuse or concurrent exchange detected for user {UserId}, family {FamilyId}.",
                        stored.UserId, stored.FamilyId);
                return null;
            }
        }
        else
        {
            stored.ConsumedOn = now;
            await _store.SaveRefreshTokenAsync(stored, cancellationToken);
            rotated = await CreateRefreshTokenAsync(
                user.Id, stored.FamilyId, stored.SessionId, audience, stored.Scope,
                clientIp, userAgent, policy, cancellationToken, stored.ClientId);
        }

        string accessToken = await CreateAccessTokenAsync(
            user,
            audience,
            stored.Scope,
            stored.SessionId,
            actor: null,
            policy,
            cancellationToken);

        return BuildResponse(accessToken, rotated, stored.Scope, user, policy);
    }



    /// <summary>
    /// Issues a delegated token for a backend service acting on a user's behalf.
    /// The subject stays the user, so downstream authorization and audit trails see
    /// the real actor; the <c>act</c> claim records which service made the call.
    /// No refresh token is issued &mdash; a service re-exchanges when it needs one.
    /// </summary>
    public async Task<CloudLoginTokenResponse?> ExchangeAsync(
        string subjectToken,
        string requestedAudience,
        string clientId,
        string clientSecret,
        Func<Guid, CancellationToken, Task<CloudUser?>> userLookup,
        CancellationToken cancellationToken = default)
    {
        ClientAuthenticationResult authentication = await _clients.AuthenticateAsync(
            clientId, clientSecret, cancellationToken);

        if (authentication.Client is not { } authenticated)
        {
            _logger.LogWarning("Token exchange rejected for client {ClientId}: {Reason}.", clientId, authentication.Failure);
            return null;
        }

        return await ExchangeAsync(subjectToken, requestedAudience, authenticated, userLookup, cancellationToken);
    }

    public async Task<CloudLoginTokenResponse?> ExchangeAsync(
        string subjectToken,
        string requestedAudience,
        ClientIdentity client,
        Func<Guid, CancellationToken, Task<CloudUser?>> userLookup,
        CancellationToken cancellationToken = default)
    {
        string clientId = client.ClientId;

        // Exchanging is for backends: a key holder may act for the user towards another service. A public client never can.
        if (client.IsPublic)
            return null;

        EnsureAudience(requestedAudience);

        if (!await _clients.IsAllowedAsync(requestedAudience, cancellationToken))
        {
            _logger.LogWarning("Token exchange rejected: audience {Audience} is blocked or cannot be checked right now.", requestedAudience);
            return null;
        }

        // The subject token must be one that was issued *to this client*. Skipping this
        // would let a service delegate a token minted for some other service, reaching
        // audiences it was never granted.
        ClaimsPrincipal? principal = await ValidateAccessTokenAsync(
            subjectToken,
            client.Audience,
            cancellationToken);

        if (principal is null)
            return null;

        string? subject = principal.FindFirst(CloudLoginClaims.Subject)?.Value;

        if (!Guid.TryParse(subject, out Guid userId) || userId == Guid.Empty)
            return null;

        CloudUser? user = await userLookup(userId, cancellationToken);

        if (user is null || user.IsLocked)
            return null;

        string sessionId = principal.FindFirst(CloudLoginClaims.SessionId)?.Value ?? NewOpaqueToken(16);
        TokenPolicy policy = DefaultPolicy;

        string accessToken = await CreateAccessTokenAsync(
            user,
            requestedAudience,
            scope: null,
            sessionId,
            actor: client.ClientId,
            policy,
            cancellationToken);

        await AuditIssuedAsync(user.Id, client.ClientId, requestedAudience, cancellationToken);

        return BuildResponse(accessToken, refreshToken: null, scope: null, user, policy);
    }

    /// <summary>
    /// Validates a CloudLogin access token and returns its principal, or
    /// <see langword="null"/> when the token is not valid for any reason.
    /// </summary>
    public async Task<ClaimsPrincipal?> ValidateAccessTokenAsync(
        string token,
        string? audience,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = audience is not null,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = await _keyManager.GetValidationKeysAsync(cancellationToken),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ClockSkew = _options.ClockSkew,
            NameClaimType = CloudLoginClaims.Name,
            RequireSignedTokens = true,
            RequireExpirationTime = true
        };

        TokenValidationResult result = await _handler.ValidateTokenAsync(token, parameters);

        if (!result.IsValid)
        {
            _logger.LogDebug(result.Exception, "Access token validation failed.");
            return null;
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _store.RevokeSessionAsync(sessionId, cancellationToken);

    public Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _store.RevokeUserAsync(userId, cancellationToken);

    public async Task<bool> IsSessionActiveAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _store is ICloudLoginSessionOwnerLookup lookup && await lookup.IsSessionActiveAsync(sessionId, cancellationToken);

    public async Task<bool> OwnsSessionAsync(string sessionId, Guid userId, CancellationToken cancellationToken = default) =>
        _store is ICloudLoginSessionOwnerLookup lookup && (await lookup.GetSessionOwnersAsync(sessionId, cancellationToken)).Contains(userId);

    public async Task<bool> RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default,
        ClientIdentity? client = null)
    {
        CloudLoginRefreshToken? stored = await _store.FindRefreshTokenAsync(
            HashToken(refreshToken),
            cancellationToken);

        if (stored is null)
            return false;

        if (!string.IsNullOrEmpty(stored.ClientId) && !string.Equals(stored.ClientId, client?.ClientId, StringComparison.Ordinal))
            return false;

        await _store.RevokeFamilyAsync(stored.FamilyId, cancellationToken);
        return true;
    }

    private async Task<string> CreateAccessTokenAsync(
        CloudUser user,
        string audience,
        string? scope,
        string sessionId,
        string? actor,
        TokenPolicy policy,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        (SigningCredentials credentials, string keyId) = await _keyManager.GetSigningCredentialsAsync(cancellationToken);

        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [CloudLoginClaims.Subject] = user.Id.ToString(),
            [CloudLoginClaims.SessionId] = sessionId,
            [CloudLoginClaims.TokenId] = NewOpaqueToken(16),
            [CloudLoginClaims.IsGlobalAdmin] = user.IsGlobalAdmin
        };

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            claims[CloudLoginClaims.Name] = user.DisplayName;

        if (user.PrimaryEmailAddress?.Input is { Length: > 0 } email)
            claims[CloudLoginClaims.Email] = email;

        if (!string.IsNullOrWhiteSpace(scope))
            claims[CloudLoginClaims.Scope] = scope;

        if (!string.IsNullOrWhiteSpace(actor))
            claims[CloudLoginClaims.Actor] = new Dictionary<string, object> { ["sub"] = actor };

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(policy.AccessLifetime),
            SigningCredentials = credentials,
            Claims = claims,
            TokenType = CloudLoginTokenTypes.AccessToken
        };

        return _handler.CreateToken(descriptor);
    }

    private async Task<string> CreateRefreshTokenAsync(
        Guid userId,
        string familyId,
        string sessionId,
        string audience,
        string? scope,
        string? clientIp,
        string? userAgent,
        TokenPolicy policy,
        CancellationToken cancellationToken,
        string? clientId = null)
    {
        // 32 bytes of CSPRNG output. The token carries no structure by design:
        // it is a lookup handle, so there is nothing in it to forge or tamper with.
        (string raw, CloudLoginRefreshToken record) = CreateRefreshTokenRecord(
            userId, familyId, sessionId, audience, scope, clientIp, userAgent, policy, clientId);
        await _store.SaveRefreshTokenAsync(record, cancellationToken);
        return raw;
    }

    private (string Raw, CloudLoginRefreshToken Record) CreateRefreshTokenRecord(
        Guid userId,
        string familyId,
        string sessionId,
        string audience,
        string? scope,
        string? clientIp,
        string? userAgent,
        TokenPolicy policy,
        string? clientId = null)
    {
        string raw = NewOpaqueToken(32);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CloudLoginRefreshToken record = new()
        {
            TokenHash = HashToken(raw),
            UserId = userId,
            FamilyId = familyId,
            SessionId = sessionId,
            Audience = audience,
            Scope = scope,
            ClientId = clientId,
            CreatedOn = now,
            ExpiresOn = now.Add(policy.RefreshLifetime),
            CreatedByIp = clientIp,
            UserAgent = Truncate(userAgent, 256),
            ttl = (int)policy.RefreshLifetime.TotalSeconds + (int)TimeSpan.FromDays(1).TotalSeconds
        };

        record.SetId(Guid.NewGuid());
        return (raw, record);
    }

    private CloudLoginTokenResponse BuildResponse(
        string accessToken,
        string? refreshToken,
        string? scope,
        CloudUser user,
        TokenPolicy policy) =>
        new()
        {
            AccessToken = accessToken,
            ExpiresIn = (int)policy.AccessLifetime.TotalSeconds,
            ExpiresOn = DateTimeOffset.UtcNow.Add(policy.AccessLifetime),
            IssuedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RefreshToken = refreshToken,
            Scope = scope,
            User = CloudLoginTransportSecurity.ForTransport(user)
        };

    private static void EnsureAudience(string audience)
    {
        if (string.IsNullOrWhiteSpace(audience) || audience.Length > 256)
            throw new InvalidOperationException("An audience is required; an unscoped token is valid everywhere.");
    }

    private async Task AuditIssuedAsync(Guid userId, string? clientId, string audience, CancellationToken cancellationToken)
    {
        if (audit is null)
            return;

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.TokenIssued,
            UserId = userId,
            ClientId = clientId,
            Data = new Dictionary<string, string> { ["Audience"] = audience }
        }, cancellationToken);
    }

    private static string NewOpaqueToken(int bytes) =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));

    private static string HashToken(string token) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
