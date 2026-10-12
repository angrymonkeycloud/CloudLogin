using System.Net;

namespace AngryMonkey.CloudLogin.Cli;

public sealed record BrowserPage(Uri Url, HttpStatusCode Status, string Body)
{
    /// <summary>The browser was handed to the application: the address is the application's own scheme, which no web page answers.</summary>
    public bool ReachedApplication => Url.Scheme is not ("http" or "https");

    public string? Query(string name)
    {
        foreach (string pair in Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);

            if (string.Equals(key, name, StringComparison.Ordinal))
                return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
        }

        return null;
    }
}

/// <summary>
/// Stands in for the system browser the native application signs in with: its own cookie jar, redirects followed one hop at a time, and
/// a stop at the application's custom scheme, exactly where the operating system would hand the address back to the application.
/// </summary>
public sealed class BrowserSession : IDisposable
{
    private const int MaxRedirects = 15;

    private readonly HttpClient _client;

    public BrowserSession(HttpMessageHandler transport, CookieContainer cookies, TimeSpan timeout)
    {
        Cookies = cookies;
        _client = new HttpClient(new CookieJarHandler(cookies) { InnerHandler = transport }) { Timeout = timeout };
    }

    public CookieContainer Cookies { get; }

    public async Task<BrowserPage> NavigateAsync(string url, HttpMethod? method = null, IReadOnlyDictionary<string, string>? form = null, CancellationToken cancellationToken = default)
    {
        Uri current = new(url, UriKind.Absolute);
        method ??= HttpMethod.Get;

        for (int hop = 0; ; hop++)
        {
            using HttpRequestMessage request = new(method, current);

            if (form is not null && method != HttpMethod.Get)
                request.Content = new FormUrlEncodedContent(form);

            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (hop >= MaxRedirects)
                    throw new HttpRequestException($"Stopped after {MaxRedirects} redirects at {current}.");

                Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);

                if (next.Scheme is not ("http" or "https"))
                    return new BrowserPage(next, response.StatusCode, string.Empty);

                current = next;

                if (response.StatusCode is not (HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                {
                    method = HttpMethod.Get;
                    form = null;
                }

                continue;
            }

            return new BrowserPage(current, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    public void Dispose() => _client.Dispose();
}
