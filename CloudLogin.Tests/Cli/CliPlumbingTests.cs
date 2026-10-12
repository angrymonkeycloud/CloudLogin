using System.Net;
using System.Text;
using AngryMonkey.CloudLogin.Cli;

namespace AngryMonkey.CloudLogin.Tests.Cli;

public class CookieJarHandlerTests
{
    private static HttpClient Client(ScriptedTransport transport, CookieContainer cookies) => new(new CookieJarHandler(cookies) { InnerHandler = new NonDisposingHandler(transport) });

    [Fact]
    public async Task ACookieTheServerSets_IsSentBackToThatServer()
    {
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Get, "app.test/set", _ =>
            {
                HttpResponseMessage response = new(HttpStatusCode.OK);
                response.Headers.Add("Set-Cookie", "session=abc; path=/; secure; httponly");
                return response;
            })
            .On(HttpMethod.Get, "app.test/use", HttpStatusCode.OK);
        CookieContainer cookies = new();
        using HttpClient client = Client(transport, cookies);

        await client.GetAsync("https://app.test/set");
        await client.GetAsync("https://app.test/use");

        Assert.Equal("session=abc", transport.Requests[1].Header("Cookie"));
    }

    [Fact]
    public async Task ACookie_IsNotSentToAnotherHost()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "other.test/", HttpStatusCode.OK);
        CookieContainer cookies = new();
        cookies.Add(new Cookie("session", "abc", "/", "app.test"));
        using HttpClient client = Client(transport, cookies);

        await client.GetAsync("https://other.test/");

        Assert.Null(transport.Requests[0].Header("Cookie"));
    }

    [Fact]
    public async Task ASecureCookie_IsNotSentOverPlainHttp()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/", HttpStatusCode.OK);
        CookieContainer cookies = new();
        cookies.Add(new Cookie("session", "abc", "/", "app.test") { Secure = true });
        using HttpClient client = Client(transport, cookies);

        await client.GetAsync("http://app.test/");

        Assert.Null(transport.Requests[0].Header("Cookie"));
    }

    [Fact]
    public async Task AnExpiredSetCookie_RemovesTheCookie()
    {
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Get, "app.test/clear", _ =>
            {
                HttpResponseMessage response = new(HttpStatusCode.OK);
                response.Headers.Add("Set-Cookie", "session=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/");
                return response;
            })
            .On(HttpMethod.Get, "app.test/use", HttpStatusCode.OK);
        CookieContainer cookies = new();
        cookies.Add(new Cookie("session", "abc", "/", "app.test"));
        using HttpClient client = Client(transport, cookies);

        await client.GetAsync("https://app.test/clear");
        await client.GetAsync("https://app.test/use");

        Assert.Null(transport.Requests[1].Header("Cookie"));
    }

    [Fact]
    public async Task AMalformedSetCookie_IsIgnored_NotFatal()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/", _ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Set-Cookie", "=;;;=");
            return response;
        });
        using HttpClient client = Client(transport, new CookieContainer());

        HttpResponseMessage result = await client.GetAsync("https://app.test/");

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task ACookieHeaderAlreadyOnTheRequest_IsReplacedByTheJars()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/", HttpStatusCode.OK);
        CookieContainer cookies = new();
        cookies.Add(new Cookie("session", "jar", "/", "app.test"));
        using HttpClient client = Client(transport, cookies);
        using HttpRequestMessage request = new(HttpMethod.Get, "https://app.test/");
        request.Headers.TryAddWithoutValidation("Cookie", "session=forged");

        await client.SendAsync(request);

        Assert.Equal("session=jar", transport.Requests[0].Header("Cookie"));
    }

    [Fact]
    public void TheHandlerNeedsAJar() => Assert.Throws<ArgumentNullException>(() => new CookieJarHandler(null!));
}

public class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cloudlogin-store-tests", Guid.NewGuid().ToString("N"));

    private string File(string name = "session.json") => Path.Combine(_directory, name);

    private static CliSession Session(params CliCookie[] cookies) =>
        new("https://app.test", "blusky", new CliUser(Guid.NewGuid(), "Ada Lovelace", "ada@example.com"), cookies, [new CliCookie("authority", "x", "login.test", "/", null, true, true)], DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task ASavedSession_LoadsBackUnchanged()
    {
        SessionStore store = new(File(), new ScramblingProtector());
        CliSession saved = Session(new CliCookie("app.session", "abc", "app.test", "/", DateTimeOffset.UtcNow.AddDays(1), true, true));

        await store.SaveAsync(saved);
        SessionLoadResult loaded = await store.LoadAsync();

        Assert.Equal(SessionLoadStatuses.Loaded, loaded.Status);
        Assert.Equal(saved.ApplicationUrl, loaded.Session!.ApplicationUrl);
        Assert.Equal(saved.User, loaded.Session.User);
        Assert.Equal(saved.ApplicationCookies.Single().Value, loaded.Session.ApplicationCookies.Single().Value);
        Assert.Equal(saved.BrowserCookies.Single().Name, loaded.Session.BrowserCookies.Single().Name);
    }

    [Fact]
    public async Task TheFileOnDisk_IsWhatTheProtectorProduced_NotPlainText()
    {
        SessionStore store = new(File(), new ScramblingProtector());
        await store.SaveAsync(Session(new CliCookie("app.session", "secret-cookie-value", "app.test", "/", null, true, true)));

        string raw = Encoding.UTF8.GetString(await System.IO.File.ReadAllBytesAsync(File()));

        Assert.DoesNotContain("secret-cookie-value", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionWrittenByAnotherProtector_IsUnreadable()
    {
        await new SessionStore(File(), new ScramblingProtector()).SaveAsync(Session());

        SessionLoadResult loaded = await new SessionStore(File(), new FilePermissionSessionProtector()).LoadAsync();

        Assert.Equal(SessionLoadStatuses.Unreadable, loaded.Status);
    }

    [Fact]
    public async Task NoFile_IsMissing() =>
        Assert.Equal(SessionLoadStatuses.Missing, (await new SessionStore(File(), new ScramblingProtector()).LoadAsync()).Status);

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"ApplicationUrl\":\"\",\"Scheme\":\"blusky\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task ADamagedFile_IsUnreadable(string contents)
    {
        Directory.CreateDirectory(_directory);
        await System.IO.File.WriteAllTextAsync(File(), contents);

        Assert.Equal(SessionLoadStatuses.Unreadable, (await new SessionStore(File(), new FilePermissionSessionProtector()).LoadAsync()).Status);
    }

    [Fact]
    public async Task Saving_CreatesTheFolder_AndLeavesNoTemporaryFile()
    {
        string nested = Path.Combine(_directory, "a", "b", "session.json");
        await new SessionStore(nested, new ScramblingProtector()).SaveAsync(Session());

        Assert.True(System.IO.File.Exists(nested));
        Assert.False(System.IO.File.Exists(nested + ".tmp"));
    }

    [Fact]
    public async Task Saving_ReplacesAnEarlierSession()
    {
        SessionStore store = new(File(), new ScramblingProtector());
        await store.SaveAsync(Session(new CliCookie("app.session", "first", "app.test", "/", null, true, true)));
        await store.SaveAsync(Session(new CliCookie("app.session", "second", "app.test", "/", null, true, true)));

        Assert.Equal("second", (await store.LoadAsync()).Session!.ApplicationCookies.Single().Value);
    }

    [Fact]
    public async Task Delete_RemovesTheSession_AndReportsWhetherOneExisted()
    {
        SessionStore store = new(File(), new ScramblingProtector());
        await store.SaveAsync(Session());

        Assert.True(store.Delete());
        Assert.False(store.Delete());
        Assert.Equal(SessionLoadStatuses.Missing, (await store.LoadAsync()).Status);
    }

    [Fact]
    public void TheStoreNeedsAPath() => Assert.Throws<ArgumentNullException>(() => new SessionStore(null!, new ScramblingProtector()));

    [Fact]
    public async Task SavingNull_IsRefused() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => new SessionStore(File(), new ScramblingProtector()).SaveAsync(null!));

    [Fact]
    public void Capture_KeepsLiveCookies_WithTheirFlagsAndExpiry()
    {
        CookieContainer container = new();
        DateTime expiry = DateTime.UtcNow.AddHours(3);
        container.Add(new Cookie("live", "1", "/path", "app.test") { Secure = true, HttpOnly = true, Expires = expiry });

        CliCookie cookie = Assert.Single(CliSession.Capture(container));

        Assert.Equal("live", cookie.Name);
        Assert.Equal("/path", cookie.Path);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(expiry, cookie.ExpiresOn!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Capture_LeavesOutExpiredCookies()
    {
        CookieContainer container = new();
        container.Add(new Cookie("gone", "1", "/", "app.test") { Expires = DateTime.UtcNow.AddMinutes(-1) });
        container.Add(new Cookie("kept", "1", "/", "app.test"));

        Assert.Equal(["kept"], CliSession.Capture(container).Select(cookie => cookie.Name));
    }

    [Fact]
    public void Restore_RebuildsAContainerTheServerRecognizes()
    {
        CookieContainer container = CliSession.Restore([new CliCookie("app.session", "abc", "app.test", "/", DateTimeOffset.UtcNow.AddDays(1), true, true)]);

        Assert.Equal("app.session=abc", container.GetCookieHeader(new Uri("https://app.test/anything")));
        Assert.Equal(string.Empty, container.GetCookieHeader(new Uri("http://app.test/")));
    }

    [Fact]
    public void Restore_DropsCookiesThatExpiredSinceTheyWereSaved()
    {
        CookieContainer container = CliSession.Restore([new CliCookie("old", "abc", "app.test", "/", DateTimeOffset.UtcNow.AddSeconds(-1), false, false)]);
        Assert.Equal(0, container.Count);
    }

    [Fact]
    public void Restore_GivesAnEmptyPathTheRoot()
    {
        CookieContainer container = CliSession.Restore([new CliCookie("c", "v", "app.test", "", null, false, false)]);
        Assert.Equal("c=v", container.GetCookieHeader(new Uri("https://app.test/deep/path")));
    }

    [Fact]
    public void CaptureThenRestore_RoundTrips()
    {
        CookieContainer original = new();
        original.Add(new Cookie("a", "1", "/", "app.test") { Secure = true });
        original.Add(new Cookie("b", "2", "/", "login.test"));

        CookieContainer restored = CliSession.Restore(CliSession.Capture(original));

        Assert.Equal(original.GetCookieHeader(new Uri("https://app.test/")), restored.GetCookieHeader(new Uri("https://app.test/")));
        Assert.Equal(original.GetCookieHeader(new Uri("https://login.test/")), restored.GetCookieHeader(new Uri("https://login.test/")));
    }

    [Fact]
    public void ThePlatformProtector_IsTheWindowsUserOneOnWindows()
    {
        ISessionProtector protector = SessionProtectors.ForCurrentPlatform();

        if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsUserSessionProtector>(protector);
        else
            Assert.IsType<FilePermissionSessionProtector>(protector);
    }

    [Fact]
    public void TheWindowsProtector_RoundTrips_AndHidesTheData()
    {
        if (!OperatingSystem.IsWindows())
            return;

        WindowsUserSessionProtector protector = new();
        byte[] data = Encoding.UTF8.GetBytes("cookie-value-to-hide");
        byte[] hidden = protector.Protect(data);

        Assert.DoesNotContain("cookie-value-to-hide", Encoding.UTF8.GetString(hidden), StringComparison.Ordinal);
        Assert.Equal(data, protector.Unprotect(hidden));
    }

    [Fact]
    public void TheWindowsProtector_RefusesDataItDidNotProtect()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => new WindowsUserSessionProtector().Unprotect(Encoding.UTF8.GetBytes("plain")));
    }
}

public class BrowserSessionTests
{
    private static BrowserSession Browser(ScriptedTransport transport, CookieContainer? cookies = null) =>
        new(new NonDisposingHandler(transport), cookies ?? new CookieContainer(), TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Redirects_AreFollowedOneHopAtATime()
    {
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Get, "a.test/start", _ => ScriptedTransport.Redirect("https://b.test/next"))
            .On(HttpMethod.Get, "b.test/next", _ => ScriptedTransport.Redirect("/final"))
            .On(HttpMethod.Get, "b.test/final", HttpStatusCode.OK, "done");
        using BrowserSession browser = Browser(transport);

        BrowserPage page = await browser.NavigateAsync("https://a.test/start");

        Assert.Equal("https://b.test/final", page.Url.ToString());
        Assert.Equal("done", page.Body);
        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    public async Task AHandOffToTheApplicationsScheme_StopsTheBrowserThere()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/callback", _ => ScriptedTransport.Redirect("blusky://auth/callback?handoff=h&state=s"));
        using BrowserSession browser = Browser(transport);

        BrowserPage page = await browser.NavigateAsync("https://app.test/auth/callback");

        Assert.True(page.ReachedApplication);
        Assert.Equal("h", page.Query("handoff"));
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task APostRedirectedWithA302_BecomesAGet()
    {
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Post, "a.test/form", _ => ScriptedTransport.Redirect("/after"))
            .On(HttpMethod.Get, "a.test/after", HttpStatusCode.OK);
        using BrowserSession browser = Browser(transport);

        await browser.NavigateAsync("https://a.test/form", HttpMethod.Post, new Dictionary<string, string> { ["x"] = "1" });

        Assert.Equal(HttpMethod.Get, transport.Requests[1].Method);
        Assert.Equal(string.Empty, transport.Requests[1].Body);
    }

    [Fact]
    public async Task APostRedirectedWithA307_StaysAPostWithItsForm()
    {
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Post, "a.test/form", _ => ScriptedTransport.Redirect("/again", HttpStatusCode.TemporaryRedirect))
            .On(HttpMethod.Post, "a.test/again", HttpStatusCode.OK);
        using BrowserSession browser = Browser(transport);

        await browser.NavigateAsync("https://a.test/form", HttpMethod.Post, new Dictionary<string, string> { ["x"] = "1" });

        Assert.Equal(HttpMethod.Post, transport.Requests[1].Method);
        Assert.Equal("1", transport.Requests[1].Form("x"));
    }

    [Fact]
    public async Task ARedirectLoop_IsStopped()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "a.test/loop", _ => ScriptedTransport.Redirect("/loop"));
        using BrowserSession browser = Browser(transport);

        await Assert.ThrowsAsync<HttpRequestException>(() => browser.NavigateAsync("https://a.test/loop"));
        Assert.Equal(16, transport.Requests.Count);
    }

    [Fact]
    public async Task CookiesSetOnTheWay_AreCarriedToLaterHops()
    {
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Get, "a.test/one", _ =>
            {
                HttpResponseMessage response = ScriptedTransport.Redirect("/two");
                response.Headers.Add("Set-Cookie", "correlation=c1; path=/");
                return response;
            })
            .On(HttpMethod.Get, "a.test/two", HttpStatusCode.OK);
        CookieContainer cookies = new();
        using BrowserSession browser = Browser(transport, cookies);

        await browser.NavigateAsync("https://a.test/one");

        Assert.Equal("correlation=c1", transport.Requests[1].Header("Cookie"));
        Assert.Equal(1, cookies.Count);
    }

    [Fact]
    public async Task ARedirectWithoutALocation_IsTheFinalPage()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "a.test/", HttpStatusCode.Found);
        using BrowserSession browser = Browser(transport);

        BrowserPage page = await browser.NavigateAsync("https://a.test/");

        Assert.Equal(HttpStatusCode.Found, page.Status);
    }

    [Fact]
    public async Task AnErrorPage_IsReturned_NotThrown()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "a.test/", HttpStatusCode.Forbidden, "no");
        using BrowserSession browser = Browser(transport);

        BrowserPage page = await browser.NavigateAsync("https://a.test/");

        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.Equal("no", page.Body);
    }

    [Theory]
    [InlineData("https://x.test/?referer=cltx%3Aabc", "referer", "cltx:abc")]
    [InlineData("https://x.test/?a=1&b=two+words", "b", "two words")]
    [InlineData("https://x.test/?flag", "flag", "")]
    [InlineData("https://x.test/?a=1", "missing", null)]
    [InlineData("https://x.test/", "a", null)]
    public void Query_ReadsAParameter(string url, string name, string? expected) =>
        Assert.Equal(expected, new BrowserPage(new Uri(url), HttpStatusCode.OK, string.Empty).Query(name));

    [Theory]
    [InlineData("https://x.test/", false)]
    [InlineData("http://x.test/", false)]
    [InlineData("blusky://auth/callback", true)]
    [InlineData("myapp://x", true)]
    public void ReachedApplication_IsAnyNonWebScheme(string url, bool expected) =>
        Assert.Equal(expected, new BrowserPage(new Uri(url), HttpStatusCode.OK, string.Empty).ReachedApplication);
}

public class CliJwtTests
{
    [Fact]
    public void TheShownClaims_AreRead_AndTimesAreReadable()
    {
        IReadOnlyDictionary<string, string> claims = CliJwt.ReadClaims(FakeDeployment.Jwt("person-1", "sid-7"));

        Assert.Equal("person-1", claims["sub"]);
        Assert.Equal("sid-7", claims["sid"]);
        Assert.Equal("app", claims["aud"]);
        Assert.Equal("2030-01-01 00:00:00Z", claims["exp"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("a.!!!.c")]
    [InlineData("a.bm90IGpzb24.c")]
    public void ATokenThatCannotBeRead_HasNoClaims(string token) => Assert.Empty(CliJwt.ReadClaims(token));

    [Fact]
    public void ClaimsThatAreNotShown_AreLeftOut()
    {
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"sub":"s","email":"private@example.com","name":"Ada"}""")).TrimEnd('=');
        IReadOnlyDictionary<string, string> claims = CliJwt.ReadClaims($"h.{payload}.s");

        Assert.Equal(["sub"], claims.Keys);
    }

    [Fact]
    public void AnArrayAudience_IsShownAsWritten()
    {
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"aud":["a","b"]}""")).TrimEnd('=');
        Assert.Equal("""["a","b"]""", CliJwt.ReadClaims($"h.{payload}.s")["aud"]);
    }
}
