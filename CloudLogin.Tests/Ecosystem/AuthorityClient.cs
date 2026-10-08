using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

internal sealed record ApiResult(HttpStatusCode Status, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;

    public string? Property(string name) => Json.TryGetProperty(name, out JsonElement value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;
}

/// <summary>An application's back channel to the authority: direct HTTP with the credentials it was given, no browser involved.</summary>
internal sealed class AuthorityClient(RoutingHandler network)
{
    private readonly HttpClient _http = new(network, disposeHandler: false);

    public static string Basic(string clientId, string secret) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}"));

    public async Task<ApiResult> PostAsync(string path, object? body, string? authorization = null, string? cookie = null, Dictionary<string, string>? headers = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{AuthorityHost.Origin}{path}") { Content = JsonContent.Create(body ?? new { }) };

        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

        if (cookie is not null)
            request.Headers.TryAddWithoutValidation("Cookie", cookie);

        foreach ((string name, string value) in headers ?? [])
            request.Headers.TryAddWithoutValidation(name, value);

        using HttpResponseMessage response = await _http.SendAsync(request);
        return new ApiResult(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<ApiResult> GetAsync(string path, string? authorization = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{AuthorityHost.Origin}{path}");

        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using HttpResponseMessage response = await _http.SendAsync(request);
        return new ApiResult(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public Task<ApiResult> BeginAsync(string authorization, string returnUrl, string state, string challenge, string? backChannelLogoutUri = null, string method = "S256") =>
        PostAsync("/CloudLogin/Authorize/Begin", new { returnUrl, state, codeChallenge = challenge, codeChallengeMethod = method, backChannelLogoutUri }, authorization);

    public Task<ApiResult> RedeemAsync(string? authorization, string code, string verifier, string redirectUri, string? clientId = null, string? audience = null) =>
        PostAsync("/CloudLogin/Token/Code", new { code, code_verifier = verifier, redirect_uri = redirectUri, client_id = clientId, audience }, authorization);

    public Task<ApiResult> RefreshAsync(string? authorization, string refreshToken, string? clientId = null) =>
        PostAsync("/CloudLogin/Token/Refresh", new { refresh_token = refreshToken, client_id = clientId }, authorization);
}
