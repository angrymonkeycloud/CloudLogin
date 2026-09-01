using System.Text.Json.Serialization;
using AngryMonkey.CloudLogin.Server.Core.Application;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>Kinds of records in the <c>Sessions</c> container.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SessionRecordKinds
{
    Family,
    Token
}

/// <summary>Why a session family was revoked.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SessionRevocationReasons
{
    None,
    UserSignedOut,
    AdminRevoked,
    TokenReuseDetected,
    SecurityStampChanged,
    Expired
}

/// <summary>
/// The head record of one refresh-token family in the <c>Sessions</c> container (partition key
/// <c>/familyId</c>).
/// <para>
/// The family document and every token document of that family share the partition, so token
/// rotation — consume old, create new, advance the head — executes as one Cosmos transactional
/// batch. Revocation flips the head document once and every later exchange in the family fails
/// on the head check, regardless of which token is presented.
/// </para>
/// </summary>
public sealed class SessionFamilyDocument : CloudLoginCoreDocument, IExpiringDocument
{
    /// <summary>Partition key. Equal to <see cref="CloudLoginCoreDocument.Id"/> for the head record.</summary>
    public string FamilyId { get; set; } = string.Empty;

    public SessionRecordKinds Kind { get; set; } = SessionRecordKinds.Family;

    public string UserId { get; set; } = string.Empty;

    /// <summary>Surfaces as the token's <c>sid</c> claim.</summary>
    public string SessionId { get; set; } = string.Empty;

    public string? Audience { get; set; }
    public string? Scope { get; set; }

    /// <summary>The id of the newest (only exchangeable) token document in the family.</summary>
    public string CurrentTokenId { get; set; } = string.Empty;

    public DateTimeOffset CreatedOn { get; set; }

    public bool IsRevoked { get; set; }
    public SessionRevocationReasons RevocationReason { get; set; } = SessionRevocationReasons.None;
    public DateTimeOffset? RevokedOn { get; set; }

    /// <summary>Informational only, never used for authorization.</summary>
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }

    // ── Device identification ─────────────────────────────────────────────────
    // Derived from the user agent at sign-in so the account page can show someone the devices
    // their account is signed in on. Descriptive only: a user agent is client-supplied and
    // forgeable, so none of this is ever used for an authorization decision.

    /// <summary>Human-readable device summary, for example "Chrome on Windows".</summary>
    public string? DeviceName { get; set; }

    /// <summary>Broad category: Desktop, Mobile, Tablet, or Unknown.</summary>
    public DeviceTypes DeviceType { get; set; } = DeviceTypes.Unknown;

    public string? DeviceBrowser { get; set; }
    public string? DeviceOperatingSystem { get; set; }

    /// <summary>
    /// When this session was last exchanged a token — the closest thing to "last used" without
    /// writing on every request. Updated on rotation.
    /// </summary>
    public DateTimeOffset? LastSeenOn { get; set; }

    /// <summary>Remote address observed at the most recent rotation.</summary>
    public string? LastSeenIp { get; set; }

    /// <summary>Absolute end of the family; rotation never extends it.</summary>
    public DateTimeOffset? ExpiresOn { get; set; }

    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }
}

/// <summary>
/// One refresh token generation in a family. The document id is the SHA-256 hash of the raw
/// token secret, so presenting a token is a point read and the raw value is never stored.
/// </summary>
public sealed class SessionTokenDocument : CloudLoginCoreDocument, IExpiringDocument
{
    /// <summary>Partition key: the owning family.</summary>
    public string FamilyId { get; set; } = string.Empty;

    public SessionRecordKinds Kind { get; set; } = SessionRecordKinds.Token;

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>Set exactly once, when the token is exchanged. A second presentation is reuse.</summary>
    public DateTimeOffset? ConsumedOn { get; set; }

    /// <summary>The token document that replaced this one at rotation.</summary>
    public string? ReplacedByTokenId { get; set; }

    public DateTimeOffset? ExpiresOn { get; set; }

    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }
}
