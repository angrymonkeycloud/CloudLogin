using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

public sealed record CloudLoginCodeRequest
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("code_verifier")]
    public required string CodeVerifier { get; init; }

    [JsonPropertyName("redirect_uri")]
    public required string RedirectUri { get; init; }

    [JsonPropertyName("audience")]
    public string? Audience { get; init; }

    /// <summary>Required for a public client. A confidential client authenticates with its credential instead.</summary>
    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }
}
