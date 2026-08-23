using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>Request to revoke a refresh token, or an entire session.</summary>
public sealed record CloudLoginRevokeRequest
{
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>When set, every refresh token issued for this session is revoked.</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}
