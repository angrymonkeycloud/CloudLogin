using System.Net;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

internal static class EcosystemFlow
{
    public static string Url(ConsumerHost consumer, string path) => $"{consumer.Origin}{path}";

    /// <summary>Opens the application's login URL and returns the reference the authority put in <c>referer</c>.</summary>
    public static async Task<(BrowserResponse Response, string Reference)> StartLoginAsync(TestBrowser browser, ConsumerHost consumer, string returnUrl = "/home")
    {
        BrowserResponse response = await browser.NavigateAsync(Url(consumer, $"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}"));
        string? reference = response.Query("referer");

        Assert.True(reference is not null, $"The consumer did not send the browser to the authority with a reference. Status {response.Status}, url {response.FinalUrl}, body {response.Body}");
        return (response, reference!);
    }

    /// <summary>What the login page does once the person has typed their password: post it, then follow the authority's redirect.</summary>
    public static async Task<BrowserResponse> SignInAtAuthorityAsync(TestBrowser browser, string reference, string email = AuthEcosystem.AuthorityEmail, string password = AuthEcosystem.Password)
    {
        BrowserResponse signedIn = await browser.NavigateAsync(
            $"{AuthorityHost.Origin}/CloudLogin/Login/PasswordSignIn",
            HttpMethod.Post,
            new Dictionary<string, string> { ["email"] = email, ["password"] = password },
            initiatorHost: new Uri(AuthorityHost.Origin).Host);

        Assert.True(signedIn.Status == HttpStatusCode.OK, $"The authority refused the password sign-in: {signedIn.Status} {signedIn.Body}");

        return await CompleteAtAuthorityAsync(browser, reference);
    }

    /// <summary>An already signed-in person arriving at the login page: the page asks the server to complete the redirect.</summary>
    public static async Task<BrowserResponse> CompleteAtAuthorityAsync(TestBrowser browser, string reference)
    {
        BrowserResponse complete = await browser.NavigateAsync(
            $"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer={Uri.EscapeDataString(reference)}",
            initiatorHost: new Uri(AuthorityHost.Origin).Host);

        Assert.Equal(HttpStatusCode.OK, complete.Status);
        string destination = complete.Body.StartsWith('"') ? JsonSerializer.Deserialize<string>(complete.Body)! : complete.Body;

        return await browser.NavigateAsync(destination, initiatorHost: new Uri(AuthorityHost.Origin).Host);
    }

    public static async Task SignInAtAuthorityOnlyAsync(TestBrowser browser, string email = AuthEcosystem.AuthorityEmail, string password = AuthEcosystem.Password)
    {
        BrowserResponse signedIn = await browser.NavigateAsync(
            $"{AuthorityHost.Origin}/CloudLogin/Login/PasswordSignIn",
            HttpMethod.Post,
            new Dictionary<string, string> { ["email"] = email, ["password"] = password },
            initiatorHost: new Uri(AuthorityHost.Origin).Host);

        Assert.Equal(HttpStatusCode.OK, signedIn.Status);
    }

    /// <summary>The callback URL the authority hands this browser, captured without following it.</summary>
    public static async Task<string> CallbackUrlAsync(TestBrowser browser, string reference)
    {
        BrowserResponse complete = await browser.NavigateAsync(
            $"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer={Uri.EscapeDataString(reference)}",
            initiatorHost: new Uri(AuthorityHost.Origin).Host);

        Assert.Equal(HttpStatusCode.OK, complete.Status);
        return complete.Body.StartsWith('"') ? JsonSerializer.Deserialize<string>(complete.Body)! : complete.Body;
    }

    public static (string Code, string State) CodeAndState(string callbackUrl)
    {
        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(new Uri(callbackUrl).Query);
        return (query["code"]!, query["state"]!);
    }

    public static async Task<BrowserResponse> LoginAsync(TestBrowser browser, ConsumerHost consumer, bool authorityAlreadySignedIn = false, string returnUrl = "/home")
    {
        (_, string reference) = await StartLoginAsync(browser, consumer, returnUrl);
        return authorityAlreadySignedIn ? await CompleteAtAuthorityAsync(browser, reference) : await SignInAtAuthorityAsync(browser, reference);
    }

    public static async Task<(bool Authenticated, string? SessionId)> MeAsync(TestBrowser browser, ConsumerHost consumer)
    {
        BrowserResponse response = await browser.NavigateAsync(Url(consumer, "/me"));

        if (response.Status != HttpStatusCode.OK)
            return (false, null);

        using JsonDocument body = JsonDocument.Parse(response.Body);
        return (true, body.RootElement.TryGetProperty("sid", out JsonElement sid) ? sid.GetString() : null);
    }
}
