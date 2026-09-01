using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.V3;

/// <summary>
/// RFC 8628 device authorization response. Field names follow the RFC exactly so standard
/// device-grant clients work unmodified.
/// </summary>
public sealed record V3DeviceAuthorizationResponse
{
    [JsonPropertyName("device_code")]
    public required string DeviceCode { get; init; }

    [JsonPropertyName("user_code")]
    public required string UserCode { get; init; }

    [JsonPropertyName("verification_uri")]
    public required string VerificationUri { get; init; }

    [JsonPropertyName("verification_uri_complete")]
    public required string VerificationUriComplete { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("interval")]
    public required int Interval { get; init; }
}

/// <summary>Device poll request (the RFC's token-endpoint call for this grant).</summary>
public sealed record V3DevicePollRequest
{
    [JsonPropertyName("device_code")]
    public required string DeviceCode { get; init; }
}

/// <summary>
/// Successful poll result: a single-use login request id the device exchanges through the
/// standard completion flow. RFC error outcomes are returned as
/// <c>{"error": "authorization_pending" | "slow_down" | "access_denied" | "expired_token"}</c>.
/// </summary>
public sealed record V3DevicePollSuccessResponse
{
    [JsonPropertyName("request_id")]
    public required Guid RequestId { get; init; }
}

public sealed record V3DeviceErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}

/// <summary>What the authenticated approval page shows before the person decides.</summary>
public sealed record V3DevicePendingResponse
{
    public required string UserCode { get; init; }
    public required string ClientDescription { get; init; }
    public required DateTimeOffset ExpiresOn { get; init; }
}

public sealed record V3DeviceDecisionRequest
{
    [JsonPropertyName("user_code")]
    public required string UserCode { get; init; }

    /// <summary>Explicit confirmation that the person verified the client description.</summary>
    public bool ConfirmClient { get; init; }
}
