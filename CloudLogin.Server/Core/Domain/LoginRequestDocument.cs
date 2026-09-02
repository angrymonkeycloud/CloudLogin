using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>Kinds of records in the <c>LoginRequests</c> container.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum LoginRequestKinds
{
    /// <summary>The classic one-time login handoff: created at sign-in, consumed by the relying site.</summary>
    Login,

    /// <summary>An RFC 8628 device authorization request (QR / TV sign-in).</summary>
    Device
}

/// <summary>States of a login or device authorization request.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum LoginRequestStates
{
    Pending,
    Approved,
    Denied,
    Consumed,
    Expired
}

/// <summary>
/// A short-lived login or device-authorization request in the <c>LoginRequests</c> container
/// (partition key <c>/id</c>).
/// <para>
/// Every request expires through native Cosmos TTL and is claimed, approved, and consumed
/// atomically via ETag-conditional replaces, so a request can only ever complete once no matter
/// how many callers race. For device requests only hashes of <c>device_code</c> and
/// <c>user_code</c> are stored; the document id is the device-code hash so polling is a point
/// read.
/// </para>
/// </summary>
public sealed class LoginRequestDocument : CloudLoginCoreDocument, IExpiringDocument
{
    public LoginRequestKinds Kind { get; set; }
    public LoginRequestStates State { get; set; } = LoginRequestStates.Pending;

    /// <summary>Set when the request resolves to a signed-in user (creation for Login, approval for Device).</summary>
    public string? UserId { get; set; }

    /// <summary>The sign-in profile bound to this request. URL tampering cannot change it later.</summary>
    public string? SignInProfile { get; set; }

    // ── Origin of the interactive sign-in ─────────────────────────────────────
    // Captured here because this is the only point in the flow that runs in the person's own
    // browser. A relying party redeems this request over a back channel, so by then the request
    // carries the relying party's server address and HTTP-client user agent — recording those as
    // "the device" would show someone their application's server instead of their own laptop.

    /// <summary>Remote address of the browser that completed the interactive sign-in.</summary>
    public string? OriginIp { get; set; }

    /// <summary>User agent of the browser that completed the interactive sign-in.</summary>
    public string? OriginUserAgent { get; set; }

    /// <summary>
    /// The sign-in session (<c>sid</c>) of the browser that created this request. Tokens redeemed
    /// from the request join that session, so the account page shows one device rather than one
    /// entry per application signed in to from it.
    /// </summary>
    public string? OriginSessionId { get; set; }

    // ── Device authorization (Kind = Device) ──────────────────────────────────

    /// <summary>SHA-256 of the high-entropy device code. Also the document id.</summary>
    public string? DeviceCodeHash { get; set; }

    /// <summary>SHA-256 of the normalized short user code shown to the person.</summary>
    public string? UserCodeHash { get; set; }

    /// <summary>Client/device description confirmed by the approving user.</summary>
    public string? ClientDescription { get; set; }

    /// <summary>The origin or client identifier that started the request.</summary>
    public string? ClientId { get; set; }

    /// <summary>Minimum seconds between polls; enforced server-side via <see cref="LastPolledOn"/>.</summary>
    public int PollIntervalSeconds { get; set; }

    public DateTimeOffset? LastPolledOn { get; set; }

    /// <summary>Failed user-code approval attempts; the request is denied once the limit is hit.</summary>
    public int AttemptCount { get; set; }

    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedOn { get; set; }

    /// <summary>Deterministic one-time login handoff created before device consumption.</summary>
    public string? HandoffRequestId { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    public DateTimeOffset? ExpiresOn { get; set; }

    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }
}
