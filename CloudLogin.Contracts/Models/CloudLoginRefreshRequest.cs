using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>Request to exchange a rotating refresh token for a fresh pair.</summary>
public sealed record CloudLoginRefreshRequest
{
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    /// <summary>Required for a public client, which proves itself by id alone. A confidential client authenticates with its credential instead.</summary>
    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }
}
