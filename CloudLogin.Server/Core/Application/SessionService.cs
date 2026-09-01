using System.Security.Cryptography;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>A presented refresh token was reused or its family is no longer valid.</summary>
public sealed class SessionTokenRejectedException(string reason) : InvalidOperationException(reason)
{
    /// <summary>True when the rejection was a reuse detection that revoked the whole family.</summary>
    public bool FamilyRevoked { get; init; }
}

/// <summary>
/// One device the account is (or was) signed in on. A device is a refresh-token family: signing
/// in creates one, and revoking it signs that device out without touching the others.
/// </summary>
public sealed record SignedInDevice
{
    /// <summary>The family id, used to revoke this device specifically.</summary>
    public required string DeviceId { get; init; }

    public required string Name { get; init; }
    public required DeviceTypes Type { get; init; }
    public string? Browser { get; init; }
    public string? OperatingSystem { get; init; }

    /// <summary>Address seen at sign-in, and at the most recent token exchange.</summary>
    public string? SignedInFromIp { get; init; }
    public string? LastSeenIp { get; init; }

    public required DateTimeOffset SignedInOn { get; init; }
    public DateTimeOffset? LastSeenOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }

    /// <summary>
    /// Whether this device can still act: the session is neither revoked nor past its absolute
    /// expiry. An inactive entry is kept so someone can see that a device <em>was</em> signed in
    /// and why it stopped.
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>Why an inactive device stopped, for example <c>TokenReuseDetected</c>.</summary>
    public SessionRevocationReasons RevocationReason { get; init; }

    public DateTimeOffset? RevokedOn { get; init; }

    /// <summary>True for the device making the current request, so the UI can label it.</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>The result of issuing or rotating a refresh token.</summary>
public sealed record SessionIssueResult
{
    public required string RawRefreshToken { get; init; }
    public required string FamilyId { get; init; }
    public required string SessionId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset ExpiresOn { get; init; }
    public string? Audience { get; init; }
    public string? Scope { get; init; }
}

/// <summary>
/// Refresh-token families over the <c>Sessions</c> container: issue, rotate atomically, detect
/// reuse, revoke. Raw tokens are <c>{familyId}.{secret}</c>; only the SHA-256 of the raw value
/// is ever stored, and it doubles as the token document id so every lookup is a point read.
/// </summary>
public sealed class SessionService(
    ISessionRepository repository,
    CloudLoginCoreConfiguration configuration,
    IAuditLogger audit)
{
    private readonly ISessionRepository _repository = repository;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly IAuditLogger _audit = audit;

    public async Task<SessionIssueResult> IssueFamilyAsync(
        Guid userId, string? audience = null, string? scope = null,
        string? createdByIp = null, string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string familyId = Guid.NewGuid().ToString("N");
        string sessionId = $"sess_{Guid.NewGuid():N}";

        (string rawToken, string tokenId) = MintToken(familyId);
        DateTimeOffset familyExpires = now + _configuration.SessionFamilyLifetime;
        DateTimeOffset tokenExpires = Earliest(now + _configuration.RefreshTokenLifetime, familyExpires);

        DeviceDescription device = DeviceDescription.Parse(userAgent);

        SessionFamilyDocument family = new()
        {
            Id = familyId,
            FamilyId = familyId,
            UserId = userId.ToString(),
            SessionId = sessionId,
            Audience = audience,
            Scope = scope,
            CurrentTokenId = tokenId,
            CreatedOn = now,
            CreatedByIp = createdByIp,
            UserAgent = userAgent,
            DeviceName = device.Name,
            DeviceType = device.Type,
            DeviceBrowser = device.Browser,
            DeviceOperatingSystem = device.OperatingSystem,
            LastSeenOn = now,
            LastSeenIp = createdByIp,
            ExpiresOn = familyExpires
        };

        SessionTokenDocument firstToken = new()
        {
            Id = tokenId,
            FamilyId = familyId,
            UserId = userId.ToString(),
            CreatedOn = now,
            ExpiresOn = tokenExpires
        };

        DocumentExpiry.Recompute(family, now);
        DocumentExpiry.Recompute(firstToken, now);

        await _repository.CreateFamilyAsync(family, firstToken, cancellationToken);

        return new SessionIssueResult
        {
            RawRefreshToken = rawToken,
            FamilyId = familyId,
            SessionId = sessionId,
            UserId = userId,
            ExpiresOn = tokenExpires,
            Audience = audience,
            Scope = scope
        };
    }

    /// <summary>
    /// Exchanges a refresh token for its successor. Reuse of an already-consumed token revokes
    /// the whole family before throwing.
    /// </summary>
    public async Task<SessionIssueResult> RotateAsync(string rawRefreshToken, CancellationToken cancellationToken = default) =>
        await RotateAsync(rawRefreshToken, seenFromIp: null, cancellationToken);

    /// <summary>
    /// As <see cref="RotateAsync(string, CancellationToken)"/>, additionally recording where the
    /// exchange came from so the device list can show a last-seen address.
    /// </summary>
    public async Task<SessionIssueResult> RotateAsync(string rawRefreshToken, string? seenFromIp, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (!TryParseToken(rawRefreshToken, out string familyId))
            throw new SessionTokenRejectedException("Malformed refresh token.");

        string presentedTokenId = IdentityHashing.Hash(rawRefreshToken);

        SessionFamilyDocument? family = await _repository.GetFamilyAsync(familyId, cancellationToken);
        if (family is null || family.IsRevoked || DocumentExpiry.IsExpired(family, now))
            throw new SessionTokenRejectedException("Session is no longer valid.");

        SessionTokenDocument? token = await _repository.GetTokenAsync(familyId, presentedTokenId, cancellationToken);
        if (token is null)
            throw new SessionTokenRejectedException("Unknown refresh token.");

        if (token.ConsumedOn is not null || !string.Equals(family.CurrentTokenId, presentedTokenId, StringComparison.Ordinal))
        {
            // Reuse: someone presented a token that was already exchanged. Burn the family.
            await RevokeFamilyCoreAsync(family, SessionRevocationReasons.TokenReuseDetected, now, cancellationToken);
            await _audit.LogAsync("Session.ReuseDetected", Guid.Parse(family.UserId),
                data: new Dictionary<string, string> { ["FamilyId"] = familyId }, cancellationToken: cancellationToken);

            throw new SessionTokenRejectedException("Refresh token reuse detected; session revoked.") { FamilyRevoked = true };
        }

        if (DocumentExpiry.IsExpired(token, now))
            throw new SessionTokenRejectedException("Refresh token expired.");

        (string newRawToken, string newTokenId) = MintToken(familyId);
        DateTimeOffset newTokenExpires = Earliest(now + _configuration.RefreshTokenLifetime, family.ExpiresOn ?? DateTimeOffset.MaxValue);

        token.ConsumedOn = now;
        token.ReplacedByTokenId = newTokenId;
        DocumentExpiry.Recompute(token, now); // ttl re-derived from its unchanged absolute expiry

        SessionTokenDocument newToken = new()
        {
            Id = newTokenId,
            FamilyId = familyId,
            UserId = family.UserId,
            CreatedOn = now,
            ExpiresOn = newTokenExpires
        };
        DocumentExpiry.Recompute(newToken, now);

        family.CurrentTokenId = newTokenId;
        family.LastSeenOn = now;
        family.LastSeenIp = seenFromIp ?? family.LastSeenIp;
        DocumentExpiry.Recompute(family, now); // absolute family expiry unchanged — rotation never extends it

        try
        {
            await _repository.RotateAsync(family, token, newToken, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            // A parallel exchange won the batch. Treat like reuse: this presentation is invalid.
            throw new SessionTokenRejectedException("Refresh token was exchanged concurrently.");
        }

        return new SessionIssueResult
        {
            RawRefreshToken = newRawToken,
            FamilyId = familyId,
            SessionId = family.SessionId,
            UserId = Guid.Parse(family.UserId),
            ExpiresOn = newTokenExpires,
            Audience = family.Audience,
            Scope = family.Scope
        };
    }

    public async Task RevokeFamilyAsync(string familyId, SessionRevocationReasons reason, CancellationToken cancellationToken = default)
    {
        SessionFamilyDocument? family = await _repository.GetFamilyAsync(familyId, cancellationToken);
        if (family is null || family.IsRevoked)
            return;

        await RevokeFamilyCoreAsync(family, reason, DateTimeOffset.UtcNow, cancellationToken);
    }

    // ── Devices ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The devices this account is signed in on, newest first, with the inactive ones included so
    /// someone can see a device that was signed in and why it stopped.
    /// </summary>
    /// <param name="currentSessionId">
    /// The <c>sid</c> of the caller's own session, so the list can mark which entry is the device
    /// making the request. Optional.
    /// </param>
    public async Task<List<SignedInDevice>> GetDevicesAsync(
        Guid userId, string? currentSessionId = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SessionFamilyDocument> families = await _repository.GetFamiliesForUserAsync(userId, cancellationToken);

        return
        [
            .. families
                .Select(family => ToDevice(family, currentSessionId, now))
                // Active devices first, then most recently seen: the list answers "where am I
                // signed in right now?" before "what used to be signed in?".
                .OrderByDescending(device => device.IsActive)
                .ThenByDescending(device => device.LastSeenOn ?? device.SignedInOn)
        ];
    }

    /// <summary>
    /// Signs one device out. Returns false when the id is not this user's, so a caller can never
    /// revoke another account's session by guessing an id.
    /// </summary>
    public async Task<bool> RevokeDeviceAsync(
        Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        SessionFamilyDocument? family = await _repository.GetFamilyAsync(deviceId, cancellationToken);

        if (family is null || !string.Equals(family.UserId, userId.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (family.IsRevoked)
            return true; // Already signed out; saying so is the same answer.

        await RevokeFamilyCoreAsync(family, SessionRevocationReasons.UserSignedOut, DateTimeOffset.UtcNow, cancellationToken);
        await _audit.LogAsync("Device.SignedOut", userId,
            data: new Dictionary<string, string> { ["DeviceId"] = deviceId }, cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>
    /// Signs every other device out and returns how many were revoked, leaving the caller's own
    /// session alone.
    /// <para>
    /// "Every other" is decided from the caller's own session id, read from its authentication
    /// ticket — never from a device id in the request. A client that asked to keep a device id
    /// could name someone else's, and a client that passed nothing would sign itself out in the
    /// middle of the request that asked not to be. When <paramref name="currentSessionId"/> is
    /// null there is nothing to preserve and this revokes everything, which is the honest reading
    /// of "sign out everywhere" from a session that is not a token family (the account page's own
    /// cookie session, for instance).
    /// </para>
    /// </summary>
    public async Task<int> RevokeOtherDevicesAsync(
        Guid userId, string? currentSessionId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SessionFamilyDocument> families = await _repository.GetFamiliesForUserAsync(userId, cancellationToken);
        int revoked = 0;

        foreach (SessionFamilyDocument family in families)
        {
            if (family.IsRevoked || DocumentExpiry.IsExpired(family, now))
                continue;

            if (currentSessionId is not null &&
                string.Equals(family.SessionId, currentSessionId, StringComparison.Ordinal))
                continue;

            await RevokeFamilyCoreAsync(family, SessionRevocationReasons.UserSignedOut, now, cancellationToken);
            revoked++;
        }

        await _audit.LogAsync("Device.OtherDevicesSignedOut", userId,
            data: new Dictionary<string, string> { ["Count"] = revoked.ToString() },
            cancellationToken: cancellationToken);

        return revoked;
    }

    private static SignedInDevice ToDevice(SessionFamilyDocument family, string? currentSessionId, DateTimeOffset now) => new()
    {
        DeviceId = family.FamilyId,
        Name = family.DeviceName ?? DeviceDescription.Unknown.Name,
        Type = family.DeviceType,
        Browser = family.DeviceBrowser,
        OperatingSystem = family.DeviceOperatingSystem,
        SignedInFromIp = family.CreatedByIp,
        LastSeenIp = family.LastSeenIp,
        SignedInOn = family.CreatedOn,
        LastSeenOn = family.LastSeenOn,
        ExpiresOn = family.ExpiresOn,
        IsActive = !family.IsRevoked && !DocumentExpiry.IsExpired(family, now),
        RevocationReason = family.RevocationReason,
        RevokedOn = family.RevokedOn,
        IsCurrent = currentSessionId is not null
            && string.Equals(family.SessionId, currentSessionId, StringComparison.Ordinal)
    };

    public async Task RevokeAllForUserAsync(Guid userId, SessionRevocationReasons reason, CancellationToken cancellationToken = default)
    {
        List<SessionFamilyDocument> families = await _repository.GetFamiliesForUserAsync(userId, cancellationToken);

        foreach (SessionFamilyDocument family in families.Where(candidate => !candidate.IsRevoked))
            await RevokeFamilyCoreAsync(family, reason, DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task RevokeFamilyCoreAsync(SessionFamilyDocument family, SessionRevocationReasons reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        family.IsRevoked = true;
        family.RevocationReason = reason;
        family.RevokedOn = now;
        DocumentExpiry.Recompute(family, now);

        try
        {
            await _repository.ReplaceFamilyAsync(family, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            // Someone else advanced or revoked the family concurrently; re-read and retry once.
            SessionFamilyDocument? current = await _repository.GetFamilyAsync(family.FamilyId, cancellationToken);
            if (current is null || current.IsRevoked)
                return;

            current.IsRevoked = true;
            current.RevocationReason = reason;
            current.RevokedOn = now;
            DocumentExpiry.Recompute(current, now);
            await _repository.ReplaceFamilyAsync(current, cancellationToken);
        }
    }

    private static (string RawToken, string TokenId) MintToken(string familyId)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        string rawToken = $"{familyId}.{Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        return (rawToken, IdentityHashing.Hash(rawToken));
    }

    private static bool TryParseToken(string rawToken, out string familyId)
    {
        familyId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawToken))
            return false;

        int separator = rawToken.IndexOf('.');
        if (separator <= 0 || separator == rawToken.Length - 1)
            return false;

        familyId = rawToken[..separator];
        return familyId.Length == 32 && familyId.All(char.IsAsciiHexDigitLower);
    }

    private static DateTimeOffset Earliest(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;
}
