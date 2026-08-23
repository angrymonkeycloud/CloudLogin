namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// A rotating refresh token.
/// <para>
/// Only a hash of the token is stored, for the same reason passwords are hashed:
/// a database disclosure must not yield usable credentials. The raw value exists
/// only in the response to the client that requested it.
/// </para>
/// <para>
/// Tokens rotate on every use and form a <see cref="FamilyId"/> chain. Presenting
/// a token that has already been consumed is the signature of a stolen token being
/// replayed, so it revokes the whole family rather than just failing the one call
/// &mdash; the legitimate client is forced to sign in again, and the thief gains nothing.
/// </para>
/// </summary>
public record CloudLoginRefreshToken : CloudLoginBaseRecord
{
    public CloudLoginRefreshToken() : base("RefreshToken", "RefreshToken") { }

    /// <summary>SHA-256 of the raw token, base64url encoded. The raw token is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    /// <summary>Shared by every token in one rotation chain, so theft revokes the chain.</summary>
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>Sign-in session this chain belongs to; surfaces as the "sid" claim.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Audience the tokens in this chain are scoped to.</summary>
    public string? Audience { get; set; }

    public string? Scope { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresOn { get; set; }

    /// <summary>Set when the token is exchanged. A second exchange attempt means replay.</summary>
    public DateTimeOffset? ConsumedOn { get; set; }

    public bool IsRevoked { get; set; }

    /// <summary>
    /// Recorded so a client that lost its token can be told when and from where the
    /// session was last used. Never used for authorization decisions.
    /// </summary>
    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Cosmos TTL so expired tokens are reaped automatically.</summary>
    public int ttl { get; set; } = (int)TimeSpan.FromDays(30).TotalSeconds;

    public bool IsActive(DateTimeOffset now) =>
        !IsRevoked && ConsumedOn is null && now < ExpiresOn;
}
