using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// A short-lived access token that a relying party hands to its own front end so
/// the front end can call a downstream API directly.
/// <para>
/// Deliberately narrower than <see cref="CloudLoginTokenResponse"/>: there is no
/// refresh token and no user payload. The long-lived credential stays in the
/// relying party's HttpOnly cookie, so the worst a stolen response can do is
/// expire within minutes, against one audience.
/// </para>
/// </summary>
public sealed record CloudLoginDownstreamTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>Access token lifetime in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// The audience the token was minted for. Echoed back so a client can assert it
    /// received a credential for the service it is about to call, rather than
    /// assuming the server honoured the audience it asked for.
    /// </summary>
    [JsonPropertyName("audience")]
    public required string Audience { get; init; }
}
