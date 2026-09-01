namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>Types of identity keys resolvable to a user.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum IdentityKeyTypes
{
    Email,
    Phone,
    External
}

/// <summary>
/// A permanent identity-to-user index record, stored in Azure Table Storage (table
/// <c>LoginIdentityKeys</c>), never in Cosmos.
/// <para>
/// PartitionKey is <c>{identityType}-v{hashVersion}-{bucket}</c> and RowKey is the
/// <see cref="IdentityKeyHasher">HMAC-SHA256</see> of the canonical identity string, so
/// resolution is always a single point lookup and the stored keys mean nothing to a reader
/// without the secret. Records are written with create-only conditional inserts: a collision
/// surfaces as a conflict instead of silently overwriting another user's identity. Identity keys
/// never expire - Table Storage holds no expiring records.
/// </para>
/// <para>
/// The canonical value itself is deliberately <em>not</em> stored. A plaintext address beside its
/// hash would defeat the keyed hash entirely, and nothing needs it: every lookup arrives with the
/// value in hand and re-derives the key, and the answer a caller wants - which user, which
/// contact - is the <see cref="UserId"/> and <see cref="ContactId"/> columns.
/// </para>
/// </summary>
public sealed class IdentityKey
{
    public IdentityKeyTypes Type { get; set; }

    /// <summary>The HMAC of the canonical identity string. Also the RowKey.</summary>
    public string Hash { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    /// <summary>
    /// The immutable contact this identity belongs to, for email and phone identities. Optional
    /// for external identities, which may be linked to an account without being tied to one of
    /// its contact points.
    /// </summary>
    public Guid? ContactId { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>Version of this entity's stored column layout.</summary>
    public int SchemaVersion { get; set; } = CloudLoginCoreSchema.CurrentVersion;

    /// <summary>Version of the keyed-hash construction that produced <see cref="Hash"/>.</summary>
    public int HashVersion { get; set; } = IdentityKeyHasher.CurrentHashVersion;

    /// <summary>Version of the normalization that produced the canonical string behind <see cref="Hash"/>.</summary>
    public int NormalizationVersion { get; set; } = IdentityKeyHasher.CurrentNormalizationVersion;

    public string TablePartitionKey => PartitionKeyFor(Type, HashVersion, Hash);
    public string TableRowKey => Hash;

    /// <summary>
    /// The partition key for an identity: type and hash version up front so a future hash or
    /// normalization change lands in its own partitions instead of colliding with today's rows.
    /// </summary>
    public static string PartitionKeyFor(IdentityKeyTypes type, int hashVersion, string hash) =>
        $"{type}-v{hashVersion}-{IdentityKeyHasher.Bucket(hash)}";

    /// <summary>Canonical form of an email identity: <c>email:{lowercased trimmed address}</c>.</summary>
    public static string CanonicalEmail(string normalizedEmail) => $"email:{normalizedEmail}";

    /// <summary>Canonical form of a phone identity: <c>phone:{E.164 or digits}</c>.</summary>
    public static string CanonicalPhone(string normalizedPhone) => $"phone:{normalizedPhone}";

    /// <summary>
    /// Canonical form of an external identity: <c>ext:{issuer}|{subject}</c> - the provider's own
    /// stable namespace plus the subject it assigned. Never an email address: an email is
    /// something a provider reports and a person can change, while the subject is what the
    /// provider guarantees to keep pointing at the same account.
    /// </summary>
    public static string CanonicalExternal(string issuer, string subject) => $"ext:{issuer}|{subject}";

    /// <summary>The identity type implied by a canonical string, used when claiming from a canonical value alone.</summary>
    public static IdentityKeyTypes TypeOf(string canonicalValue) => canonicalValue switch
    {
        _ when canonicalValue.StartsWith("phone:", StringComparison.Ordinal) => IdentityKeyTypes.Phone,
        _ when canonicalValue.StartsWith("ext:", StringComparison.Ordinal) => IdentityKeyTypes.External,
        _ => IdentityKeyTypes.Email
    };
}
