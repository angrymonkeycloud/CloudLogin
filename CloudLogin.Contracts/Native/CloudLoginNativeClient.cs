using System.Net;
using System.Net.Http.Json;

namespace AngryMonkey.CloudLogin.Native;

public enum CloudLoginNativeSessionEndResults
{
    /// <summary>The session ended at the backend and at the authority, for every application sharing it.</summary>
    Ended,

    /// <summary>There was no session to end: it had already expired or been ended elsewhere.</summary>
    NoSession,

    /// <summary>The backend or the authority could not be reached. Nothing changed; ending it again later finishes it.</summary>
    Unavailable,

    /// <summary>The backend refused the request as not coming from the application.</summary>
    Refused
}

public enum CloudLoginNativeTokenStatuses
{
    Issued,

    /// <summary>The session is over; the person signs in again.</summary>
    SessionOver,

    /// <summary>The session could not be checked or renewed right now. It is still valid; try again.</summary>
    Unavailable
}

public sealed record CloudLoginNativeTokenResult(CloudLoginNativeTokenStatuses Status, string? AccessToken = null, int ExpiresIn = 0, string? Audience = null);

/// <summary>
/// The native application's calls to its own backend. The <see cref="HttpClient"/> is the application's: its base address is the backend
/// and its handler keeps the session cookie, so the cookie never leaves the application's own HTTP stack.
/// </summary>
public sealed class CloudLoginNativeClient(HttpClient client)
{
    private static readonly System.Text.Json.JsonSerializerOptions ReadOptions = CreateReadOptions();

    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    private static System.Text.Json.JsonSerializerOptions CreateReadOptions()
    {
        System.Text.Json.JsonSerializerOptions options = CloudLoginSerialization.Options;
        options.PropertyNameCaseInsensitive = true;
        return options;
    }

    /// <summary>
    /// Redeems the handoff with the verifier only this application holds. The backend answers with the session cookie, which the client's
    /// handler keeps, and the signed-in person. Null when the handoff was refused: unknown, expired, already used, or the wrong verifier.
    /// </summary>
    public async Task<CloudUser?> ExchangeAsync(string handoff, string verifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoff);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);

        using HttpResponseMessage response = await _client.PostAsJsonAsync(CloudLoginNativeProtocol.ExchangePath, new { Handoff = handoff, Verifier = verifier }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<CloudUser>(ReadOptions, cancellationToken);
    }

    /// <summary>Ends the session for its owner everywhere: this application, the browser sign-in, and every application sharing it.</summary>
    public async Task<CloudLoginNativeSessionEndResults> EndSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, CloudLoginNativeProtocol.SessionEndPath);
            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.NoContent or HttpStatusCode.OK => CloudLoginNativeSessionEndResults.Ended,
                HttpStatusCode.Unauthorized => CloudLoginNativeSessionEndResults.NoSession,
                HttpStatusCode.Forbidden => CloudLoginNativeSessionEndResults.Refused,
                _ when IsRedirectToSignIn(response) => CloudLoginNativeSessionEndResults.NoSession,
                _ => CloudLoginNativeSessionEndResults.Unavailable
            };
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return CloudLoginNativeSessionEndResults.Unavailable;
        }
    }

    /// <summary>A current access token for <paramref name="audience"/> (the backend's own when null), renewed by the backend when needed.</summary>
    public async Task<CloudLoginNativeTokenResult> GetTokenAsync(string? audience = null, CancellationToken cancellationToken = default)
    {
        string path = string.IsNullOrWhiteSpace(audience) ? CloudLoginNativeProtocol.TokenPath : $"{CloudLoginNativeProtocol.TokenPath}?audience={Uri.EscapeDataString(audience)}";

        try
        {
            using HttpResponseMessage response = await _client.GetAsync(path, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                CloudLoginDownstreamTokenResponse? token = await response.Content.ReadFromJsonAsync<CloudLoginDownstreamTokenResponse>(cancellationToken);

                return string.IsNullOrWhiteSpace(token?.AccessToken)
                    ? new CloudLoginNativeTokenResult(CloudLoginNativeTokenStatuses.Unavailable)
                    : new CloudLoginNativeTokenResult(CloudLoginNativeTokenStatuses.Issued, token.AccessToken, token.ExpiresIn, token.Audience);
            }

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || IsRedirectToSignIn(response)
                ? new CloudLoginNativeTokenResult(CloudLoginNativeTokenStatuses.SessionOver)
                : new CloudLoginNativeTokenResult(CloudLoginNativeTokenStatuses.Unavailable);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new CloudLoginNativeTokenResult(CloudLoginNativeTokenStatuses.Unavailable);
        }
    }

    private static bool IsRedirectToSignIn(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 300 and < 400 &&
        response.Headers.Location is { } location &&
        (location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString).Contains("/auth/login", StringComparison.OrdinalIgnoreCase);
}
