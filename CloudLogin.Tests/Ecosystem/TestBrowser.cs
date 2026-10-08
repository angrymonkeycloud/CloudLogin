using System.Net;
using Microsoft.Net.Http.Headers;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

internal sealed class RoutingHandler : HttpMessageHandler
{
    public Dictionary<string, HttpMessageHandler> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string host = request.RequestUri!.Host;

        lock (Requests)
            Requests.Add($"{request.Method} {request.RequestUri.AbsoluteUri}");

        if (!Routes.TryGetValue(host, out HttpMessageHandler? handler))
            throw new HttpRequestException($"No test host answers for '{host}'.");

        return new HttpMessageInvoker(handler, disposeHandler: false).SendAsync(request, cancellationToken);
    }
}

internal sealed record BrowserResponse(Uri FinalUrl, HttpStatusCode Status, string Body, IReadOnlyList<Uri> Chain, IReadOnlyList<HttpStatusCode> Statuses)
{
    public string? Query(string name) => System.Web.HttpUtility.ParseQueryString(FinalUrl.Query)[name];

    public Uri? LastRedirectTarget => Chain.Count > 1 ? Chain[^1] : null;
}

/// <summary>
/// A browser, close enough to exercise sign-in and sign-out across hosts: a cookie jar that honours host, path, expiry and
/// SameSite, redirects followed hop by hop, and the Sec-Fetch-Site header a real browser sends.
/// </summary>
internal sealed class TestBrowser(RoutingHandler network)
{
    private sealed record StoredCookie(string Host, string Name, string Value, string Path, DateTimeOffset? ExpiresOn, SameSiteMode SameSite);

    private readonly List<StoredCookie> _jar = [];

    public IReadOnlyList<(string Host, string Name, string Value)> Cookies => [.. _jar.Select(cookie => (cookie.Host, cookie.Name, cookie.Value))];

    public DateTimeOffset? CookieExpiresOn(string host, string name) => _jar.FirstOrDefault(cookie => cookie.Host == host && cookie.Name == name)?.ExpiresOn;

    public string? CookieValue(string host, string namePart) => _jar.FirstOrDefault(cookie => cookie.Host == host && cookie.Name.Contains(namePart, StringComparison.Ordinal))?.Value;

    public void SetCookie(string host, string name, string value) => Store(new StoredCookie(host, name, value, "/", null, SameSiteMode.Lax));

    public int CookieCount(string host, string namePart) => _jar.Count(cookie => cookie.Host == host && cookie.Name.Contains(namePart, StringComparison.Ordinal));

    public void Clear() => _jar.Clear();

    public async Task<BrowserResponse> NavigateAsync(
        string url,
        HttpMethod? method = null,
        Dictionary<string, string>? form = null,
        string? initiatorHost = null,
        bool follow = true,
        Dictionary<string, string>? headers = null,
        string? json = null,
        int maxRedirects = 10)
    {
        method ??= HttpMethod.Get;
        Uri current = new(url);
        string? previousHost = initiatorHost;
        bool crossSite = initiatorHost is not null && !SameSite(initiatorHost, current.Host);
        List<Uri> chain = [current];
        List<HttpStatusCode> statuses = [];

        for (int hop = 0; ; hop++)
        {
            string fetchSite = previousHost is null ? "none" : crossSite ? "cross-site" : string.Equals(previousHost, current.Host, StringComparison.OrdinalIgnoreCase) ? "same-origin" : "same-site";

            using HttpRequestMessage request = new(method, current);
            if (headers?.ContainsKey("Sec-Fetch-Site") != true)
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);

            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");

            foreach ((string name, string value) in headers ?? [])
                if (value.Length > 0)
                    request.Headers.TryAddWithoutValidation(name, value);

            string cookieHeader = CookieHeader(current, method, fetchSite == "cross-site");

            if (cookieHeader.Length > 0)
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            if (json is not null && method != HttpMethod.Get)
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            else if (form is not null && method != HttpMethod.Get)
                request.Content = new FormUrlEncodedContent(form);

            using HttpResponseMessage response = await new HttpClient(network, disposeHandler: false).SendAsync(request);
            StoreCookies(current, response);
            statuses.Add(response.StatusCode);

            if (follow && (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location && hop < maxRedirects)
            {
                Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);

                if (next.Scheme is not ("http" or "https"))
                {
                    chain.Add(next);
                    return new BrowserResponse(next, response.StatusCode, string.Empty, chain, statuses);
                }

                if (!SameSite(current.Host, next.Host))
                    crossSite = true;

                previousHost = current.Host;
                current = next;
                chain.Add(current);
                method = response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect ? method : HttpMethod.Get;
                form = method == HttpMethod.Get ? null : form;
                continue;
            }

            return new BrowserResponse(current, response.StatusCode, await response.Content.ReadAsStringAsync(), chain, statuses);
        }
    }

    private static bool SameSite(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string CookieHeader(Uri url, HttpMethod method, bool crossSite)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return string.Join("; ", _jar
            .Where(cookie => cookie.Host == url.Host
                && url.AbsolutePath.StartsWith(cookie.Path, StringComparison.Ordinal)
                && (cookie.ExpiresOn is null || cookie.ExpiresOn > now)
                && (!crossSite || cookie.SameSite == SameSiteMode.None || (cookie.SameSite == SameSiteMode.Lax && method == HttpMethod.Get)))
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    private void StoreCookies(Uri url, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
            return;

        foreach (string value in values)
        {
            SetCookieHeaderValue parsed = SetCookieHeaderValue.Parse(value);
            DateTimeOffset? expires = parsed.MaxAge is { } maxAge ? DateTimeOffset.UtcNow + maxAge : parsed.Expires;
            Store(new StoredCookie(url.Host, parsed.Name.Value!, parsed.Value.Value ?? string.Empty, parsed.Path.HasValue ? parsed.Path.Value! : "/", expires, parsed.SameSite));
        }
    }

    private void Store(StoredCookie cookie)
    {
        _jar.RemoveAll(existing => existing.Host == cookie.Host && existing.Name == cookie.Name && existing.Path == cookie.Path);

        if (!string.IsNullOrEmpty(cookie.Value) && (cookie.ExpiresOn is null || cookie.ExpiresOn > DateTimeOffset.UtcNow))
            _jar.Add(cookie);
    }
}
