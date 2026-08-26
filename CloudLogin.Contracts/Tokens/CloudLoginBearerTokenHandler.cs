using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Attaches a downstream access token to every request on the <see cref="HttpClient"/>
/// it is registered against, fetching that token from the application's own
/// <c>/auth/token</c> endpoint.
/// <para>
/// This is how a browser or native front end calls a <em>different</em> service than the
/// one that signed it in. The alternative — relying on the other service's session
/// cookie — cannot work: that cookie belongs to another origin, is not in this client's
/// cookie jar, and is exactly the kind of ambient credential that makes cross-origin
/// calls CSRF-prone. A token is explicit, audience-scoped, and expires in minutes.
/// </para>
/// <para>
/// The client never sees a refresh token or a client secret. It holds only a short-lived
/// access token, in memory, for one audience.
/// </para>
/// </summary>
public sealed class CloudLoginBearerTokenHandler : DelegatingHandler
{
    /// <summary>
    /// Refresh this long before real expiry, so a token cannot lapse between the moment
    /// it is attached and the moment the downstream service validates it.
    /// </summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Upper bound on a body this handler is willing to buffer so it can replay the
    /// request after a token refresh. Larger payloads are still sent — they simply are
    /// not retried, because holding an arbitrary upload in memory to save one round trip
    /// is a worse trade than surfacing the 401.
    /// </summary>
    private const long MaxReplayBytes = 1024 * 1024;

    private readonly HttpClient _tokenEndpoint;
    private readonly string _audience;
    private readonly string _tokenPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    /// <param name="tokenEndpoint">
    /// A client addressed at this application's own origin. Its requests carry the
    /// application's session cookie, which is what authorizes the token request.
    /// </param>
    /// <param name="audience">The audience of the downstream service being called.</param>
    /// <param name="tokenPath">Relative path of the token endpoint.</param>
    public CloudLoginBearerTokenHandler(HttpClient tokenEndpoint, string audience, string tokenPath = "auth/token")
    {
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        _tokenEndpoint = tokenEndpoint;
        _audience = audience;
        _tokenPath = tokenPath.TrimStart('/');
    }

    /// <summary>
    /// The current downstream token, for callers that cannot route through an HTTP
    /// handler. SignalR is the case that matters: a WebSocket handshake carries no
    /// request headers, so the token has to be handed to the connection directly.
    /// Returns <see langword="null"/> when the user is not signed in.
    /// </summary>
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        GetTokenAsync(forceRefresh: false, cancellationToken);

    /// <summary>Drops the cached token, so the next request fetches a fresh one.</summary>
    public void Clear()
    {
        _accessToken = null;
        _expiresAtUtc = DateTimeOffset.MinValue;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? token = await GetTokenAsync(forceRefresh: false, cancellationToken);

        // No token means the user is not signed in here. Send the request unauthenticated
        // rather than failing locally, so the caller sees the downstream service's own
        // answer instead of an error this handler invented.
        if (token is null)
            return await base.SendAsync(request, cancellationToken);

        HttpRequestMessage? replay = await TryBufferForReplayAsync(request, cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || replay is null)
        {
            replay?.Dispose();
            return response;
        }

        // The downstream service rejected the token: it expired early, or the signing key
        // rotated. One forced refresh distinguishes that from a real authorization failure.
        string? refreshed = await GetTokenAsync(forceRefresh: true, cancellationToken);

        if (refreshed is null || string.Equals(refreshed, token, StringComparison.Ordinal))
        {
            replay.Dispose();
            return response;
        }

        response.Dispose();

        using (replay)
        {
            replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
            return await base.SendAsync(replay, cancellationToken);
        }
    }

    private async Task<string?> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && IsUsable())
            return _accessToken;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Another caller may have refreshed while this one waited.
            if (!forceRefresh && IsUsable())
                return _accessToken;

            if (forceRefresh)
                Clear();

            using HttpResponseMessage response = await _tokenEndpoint.GetAsync(
                $"{_tokenPath}?audience={Uri.EscapeDataString(_audience)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Clear();
                return null;
            }

            CloudLoginDownstreamTokenResponse? token = await response.Content
                .ReadFromJsonAsync<CloudLoginDownstreamTokenResponse>(cancellationToken);

            // A token minted for some other service is not usable here, and sending it
            // anyway would hand this user's credential to an audience that never asked
            // for it.
            if (token is null ||
                string.IsNullOrWhiteSpace(token.AccessToken) ||
                !string.Equals(token.Audience, _audience, StringComparison.Ordinal))
            {
                Clear();
                return null;
            }

            _accessToken = token.AccessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            return _accessToken;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Clear();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsUsable() =>
        !string.IsNullOrWhiteSpace(_accessToken) &&
        DateTimeOffset.UtcNow.Add(RefreshWindow) < _expiresAtUtc;

    private static async Task<HttpRequestMessage?> TryBufferForReplayAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null &&
            request.Content.Headers.ContentLength is not (>= 0 and <= MaxReplayBytes))
            return null;

        HttpRequestMessage clone = new(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (KeyValuePair<string, object?> option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        if (request.Content is not null)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(body);

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _gate.Dispose();

        base.Dispose(disposing);
    }
}
