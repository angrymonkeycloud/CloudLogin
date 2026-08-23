using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// A minted token pair. Access tokens are deliberately short lived; the refresh
/// token is the only long-lived credential and it rotates on every use.
/// </summary>
public sealed record CloudLoginTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>Access token lifetime in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// Absolute expiry, computed by the client on receipt. Not serialized by the
    /// authority &mdash; clients must not trust a server-supplied wall-clock time.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset ExpiresAtUtc { get; init; } = DateTimeOffset.MinValue;

    /// <summary>
    /// Present only for flows that issue one (interactive sign-in, native apps).
    /// Service-to-service delegation never returns a refresh token.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>Convenience view of the subject, so callers avoid re-parsing the JWT.</summary>
    [JsonPropertyName("user")]
    public CloudUser? User { get; init; }
}
