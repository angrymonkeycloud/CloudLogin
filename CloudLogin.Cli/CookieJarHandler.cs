using System.Net;

namespace AngryMonkey.CloudLogin.Cli;

/// <summary>Sends and keeps cookies for a <see cref="CookieContainer"/> over any transport, including an in-process test host.</summary>
public sealed class CookieJarHandler(CookieContainer cookies) : DelegatingHandler
{
    public CookieContainer Cookies { get; } = cookies ?? throw new ArgumentNullException(nameof(cookies));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("A request needs an absolute address.");
        string header = Cookies.GetCookieHeader(uri);

        request.Headers.Remove("Cookie");

        if (header.Length > 0)
            request.Headers.TryAddWithoutValidation("Cookie", header);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
            foreach (string value in values)
                Store(uri, value);

        return response;
    }

    private void Store(Uri uri, string setCookie)
    {
        try
        {
            Cookies.SetCookies(uri, setCookie);
        }
        catch (CookieException)
        {
        }
    }
}
