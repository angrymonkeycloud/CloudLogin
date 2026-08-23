using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>Request to exchange a rotating refresh token for a fresh pair.</summary>
public sealed record CloudLoginRefreshRequest
{
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }
}
