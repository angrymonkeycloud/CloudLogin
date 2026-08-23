namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// A persisted ES256 signing key.
/// <para>
/// The private component is never stored in the clear: it is wrapped with ASP.NET
/// Data Protection before it reaches the database, so a database disclosure alone
/// does not let an attacker mint tokens. The public component is stored separately
/// in plain form because it is published through JWKS anyway.
/// </para>
/// <para>
/// Keys have two distinct lifetimes. <see cref="SigningExpiresOn"/> is when the key
/// stops being used to <em>sign</em>; <see cref="PublishExpiresOn"/> is the later
/// moment when it stops being published for <em>verification</em>. The gap must
/// exceed the access-token lifetime, otherwise rotation would invalidate tokens
/// that are still legitimately in flight.
/// </para>
/// </summary>
public record CloudLoginSigningKey : CloudLoginBaseRecord
{
    public CloudLoginSigningKey() : base("SigningKey", "SigningKey") { }

    /// <summary>JWK "kid" &mdash; published in the token header so verifiers pick the right key.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Data Protection-wrapped PKCS#8 private key, base64 encoded.</summary>
    public string ProtectedPrivateKey { get; set; } = string.Empty;

    /// <summary>Base64url EC public key X coordinate (JWK "x").</summary>
    public string PublicX { get; set; } = string.Empty;

    /// <summary>Base64url EC public key Y coordinate (JWK "y").</summary>
    public string PublicY { get; set; } = string.Empty;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>After this moment the key is still trusted but no longer signs new tokens.</summary>
    public DateTimeOffset SigningExpiresOn { get; set; }

    /// <summary>After this moment the key leaves JWKS and tokens it signed no longer verify.</summary>
    public DateTimeOffset PublishExpiresOn { get; set; }

    /// <summary>
    /// Cosmos TTL, in seconds, so retired keys clean themselves up rather than
    /// accumulating indefinitely. Set from <see cref="PublishExpiresOn"/>.
    /// </summary>
    public int ttl { get; set; } = (int)TimeSpan.FromDays(90).TotalSeconds;

    public bool CanSign(DateTimeOffset now) => now >= CreatedOn && now < SigningExpiresOn;

    public bool CanVerify(DateTimeOffset now) => now < PublishExpiresOn;
}
