using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Service-to-service delegation request (modelled on RFC 8693 token exchange).
/// The caller authenticates with its own client credential and presents the end
/// user's access token; the authority returns a token whose subject is still the
/// user but whose <see cref="CloudLoginClaims.Actor"/> names the caller.
/// </summary>
public sealed record CloudLoginExchangeRequest
{
    /// <summary>The end user's access token that the caller received.</summary>
    [JsonPropertyName("subject_token")]
    public required string SubjectToken { get; init; }

    /// <summary>Audience the returned token should be valid for.</summary>
    [JsonPropertyName("audience")]
    public required string Audience { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}
