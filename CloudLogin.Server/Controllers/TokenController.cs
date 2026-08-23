using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Controllers;

/// <summary>
/// The authority's token endpoints.
/// <para>
/// Every route here either proves who the caller is before minting anything, or
/// publishes purely public key material. There is deliberately no endpoint that
/// turns a bare user id into a token &mdash; that would reintroduce exactly the
/// impersonation this design exists to prevent.
/// </para>
/// </summary>
[ApiController]
[Route("CloudLogin/Token")]
public sealed class TokenController(
    CloudLoginTokenService tokenService,
    CloudLoginSigningKeyManager keyManager,
    ICloudLogin server,
    IOptions<CloudLoginTokenOptions> options,
    ILogger<TokenController> logger) : ControllerBase
{
    private readonly CloudLoginTokenService _tokens = tokenService;
    private readonly CloudLoginSigningKeyManager _keys = keyManager;
    private readonly ICloudLogin _server = server;
    private readonly CloudLoginTokenOptions _options = options.Value;
    private readonly ILogger<TokenController> _logger = logger;

    /// <summary>
    /// Issues tokens for the browser session that owns the CloudLogin cookie.
    /// Used by the authority's own first-party surfaces; the cookie is the proof,
    /// so nothing about the user is taken from the request.
    /// </summary>
    [HttpPost("Session")]
    [Authorize]
    public async Task<IActionResult> FromSession(
        [FromQuery] string audience,
        [FromQuery] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        CloudUser? user = await _server.CurrentUser();

        if (user is null || user.ID == Guid.Empty || user.IsLocked)
            return Unauthorized();

        try
        {
            CloudLoginTokenResponse response = await _tokens.IssueAsync(
                user,
                audience,
                scope,
                clientIp: ClientIp(),
                userAgent: UserAgent(),
                cancellationToken: cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    /// <summary>
    /// Exchanges a single-use login request id for tokens.
    /// <para>
    /// This is how a relying party completes sign-in. It replaces the older
    /// "fetch the user, then trust its id forever" handoff: the relying party now
    /// receives a credential it can present downstream, rather than a bare
    /// identifier it would have to assert.
    /// </para>
    /// <para>
    /// Requires service-client credentials, because a login request id travels
    /// through a browser redirect and is therefore not a secret on its own.
    /// </para>
    /// </summary>
    [HttpPost("FromRequest")]
    [AllowAnonymous]
    public async Task<IActionResult> FromRequest(
        [FromQuery] Guid requestId,
        [FromQuery] string audience,
        [FromQuery] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadClientCredentials(out string clientId, out string clientSecret))
            return Unauthorized(new { error = "invalid_client" });

        if (!_options.ServiceClients.TryGetValue(clientId, out CloudLoginServiceClient? client) ||
            client.IsDisabled ||
            !client.AllowedAudiences.Contains(audience))
            return Unauthorized(new { error = "invalid_client" });

        if (!VerifyClientSecret(client, clientSecret))
            return Unauthorized(new { error = "invalid_client" });

        if (requestId == Guid.Empty)
            return BadRequest(new { error = "invalid_request" });

        // Consumes the request id: it is single use and short lived by design.
        CloudUser? user = await _server.GetUserByRequestId(requestId);

        if (user is null || user.ID == Guid.Empty || user.IsLocked)
            return Unauthorized(new { error = "invalid_grant" });

        try
        {
            CloudLoginTokenResponse response = await _tokens.IssueAsync(
                user,
                audience,
                scope,
                clientIp: ClientIp(),
                userAgent: UserAgent(),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Issued tokens to client {ClientId} for audience {Audience}.",
                clientId,
                audience);

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    /// <summary>
    /// Rotates a refresh token. Replaying a consumed token revokes its whole family,
    /// so a stolen refresh token is usable at most once before it burns the session.
    /// </summary>
    [HttpPost("Refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody] CloudLoginRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        CloudLoginTokenResponse? response = await _tokens.RefreshAsync(
            request.RefreshToken,
            async (userId, token) => await _server.GetUserById(userId),
            ClientIp(),
            UserAgent(),
            cancellationToken);

        return response is null
            ? Unauthorized(new { error = "invalid_grant" })
            : Ok(response);
    }

    /// <summary>
    /// Delegated token exchange. A backend service presents its own credentials plus
    /// the end user's access token, and receives a token that still names the user as
    /// subject but records the service in the <c>act</c> claim.
    /// </summary>
    [HttpPost("Exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange(
        [FromBody] CloudLoginExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadClientCredentials(out string clientId, out string clientSecret))
            return Unauthorized(new { error = "invalid_client" });

        CloudLoginTokenResponse? response = await _tokens.ExchangeAsync(
            request.SubjectToken,
            request.Audience,
            clientId,
            clientSecret,
            async (userId, token) => await _server.GetUserById(userId),
            request.Scope,
            cancellationToken);

        return response is null
            ? Unauthorized(new { error = "invalid_grant" })
            : Ok(response);
    }

    /// <summary>
    /// Revokes a refresh token and everything rotated from it.
    /// <para>
    /// Anonymous, because the refresh token itself is the proof: presenting it shows
    /// you hold it. Unknown tokens still report success, so this cannot be used to
    /// probe which tokens exist.
    /// </para>
    /// <para>
    /// Revoking by session id is different &mdash; a session id is not a secret, it
    /// travels in the <c>sid</c> claim of every token minted for that sign-in. So it
    /// requires an access token for that same session, otherwise anyone who observed
    /// a session id could sign that user out at will.
    /// </para>
    /// </summary>
    [HttpPost("Revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> Revoke(
        [FromBody] CloudLoginRevokeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _tokens.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!await CallerOwnsSessionAsync(request.SessionId, cancellationToken))
                return Unauthorized(new { error = "invalid_grant" });

            await _tokens.RevokeSessionAsync(request.SessionId, cancellationToken);
        }

        return NoContent();
    }

    /// <summary>
    /// Confirms the caller presented a valid access token belonging to the session it
    /// is asking to revoke, or is signed in as the user who owns it.
    /// </summary>
    private async Task<bool> CallerOwnsSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        string authorization = Request.Headers.Authorization.ToString();

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            System.Security.Claims.ClaimsPrincipal? principal = await _tokens.ValidateAccessTokenAsync(
                authorization["Bearer ".Length..].Trim(),
                audience: null,
                cancellationToken);

            if (string.Equals(
                    principal?.FindFirst(CloudLoginClaims.SessionId)?.Value,
                    sessionId,
                    StringComparison.Ordinal))
                return true;
        }

        // A user signed in to the authority may end their own sessions from the
        // account page, where there is a cookie rather than a bearer token.
        return User.Identity?.IsAuthenticated == true;
    }

    private bool VerifyClientSecret(CloudLoginServiceClient client, string secret)
    {
        byte[] expected = Convert.FromBase64String(client.SecretHash);
        byte[] actual = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret));

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private bool TryReadClientCredentials(out string clientId, out string clientSecret)
    {
        clientId = string.Empty;
        clientSecret = string.Empty;

        string? header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            string decoded = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(header["Basic ".Length..].Trim()));

            int separator = decoded.IndexOf(':');

            if (separator <= 0)
                return false;

            clientId = decoded[..separator];
            clientSecret = decoded[(separator + 1)..];

            return clientSecret.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent() => Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
        ? agent
        : null;
}

/// <summary>
/// Public discovery surface. Resource servers read these two documents to learn how
/// to verify tokens, which is what lets them validate without holding any secret.
/// </summary>
[ApiController]
public sealed class CloudLoginDiscoveryController(
    CloudLoginSigningKeyManager keyManager,
    IOptions<CloudLoginTokenOptions> options) : ControllerBase
{
    private readonly CloudLoginSigningKeyManager _keys = keyManager;
    private readonly CloudLoginTokenOptions _options = options.Value;

    [HttpGet("/.well-known/openid-configuration")]
    [AllowAnonymous]
    public IActionResult Discovery()
    {
        string issuer = _options.Issuer.TrimEnd('/');

        // Cached briefly: it changes only on configuration change, and resource
        // servers poll it on startup and on unknown-kid.
        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(new
        {
            issuer,
            jwks_uri = $"{issuer}/.well-known/jwks.json",
            token_endpoint = $"{issuer}/CloudLogin/Token/Refresh",
            introspection_endpoint = $"{issuer}/CloudLogin/Token/Exchange",
            revocation_endpoint = $"{issuer}/CloudLogin/Token/Revoke",
            id_token_signing_alg_values_supported = new[] { "ES256" },
            response_types_supported = new[] { "token" },
            subject_types_supported = new[] { "public" },
            grant_types_supported = new[] { "refresh_token", "urn:ietf:params:oauth:grant-type:token-exchange" }
        });
    }

    [HttpGet("/.well-known/jwks.json")]
    [AllowAnonymous]
    public async Task<IActionResult> Jwks(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(await _keys.GetJsonWebKeySetAsync(cancellationToken));
    }
}
