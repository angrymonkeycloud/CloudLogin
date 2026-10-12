using System.Net;
using System.Text.Json;
using AngryMonkey.CloudLogin.Server.Controllers;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>
/// The life of a native application's session after sign-in: renewing it while the person works, which happens on the backend from many
/// requests at once, and ending it from the application without a browser.
/// </summary>
public class NativeSessionLifecycleTests
{
    private const string Scheme = "blusky";
    private const string AppName = "blusky-app";
    private const string SessionCookie = AppName + ".session";
    private const string RefreshEndpoint = "POST https://login.test/CloudLogin/Token/Refresh";

    private sealed class SignedInApp(AuthEcosystem ecosystem, ConsumerHost backend)
    {
        public TestBrowser AppHttp { get; } = ecosystem.NewBrowser();
        public TestBrowser SystemBrowser { get; } = ecosystem.NewBrowser();

        public async Task SignInAsync()
        {
            string verifier = CloudLoginPkce.CreateVerifier();
            string state = CloudLoginPkce.CreateState();
            string callback = $"{Scheme}://auth/callback";

            BrowserResponse start = await SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/login?challenge={Uri.EscapeDataString(CloudLoginPkce.CreateChallenge(verifier))}&state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(callback)}");
            BrowserResponse handed = await EcosystemFlow.SignInAtAuthorityAsync(SystemBrowser, start.Query("referer")!);
            string handoff = handed.Query("handoff")!;

            BrowserResponse exchanged = await AppHttp.NavigateAsync($"{backend.Origin}/auth/native/exchange", HttpMethod.Post, json: JsonSerializer.Serialize(new NativeExchangeRequest(handoff, verifier)));
            Assert.Equal(HttpStatusCode.OK, exchanged.Status);
        }

        public string Cookie => AppHttp.CookieValue(backend.Host, SessionCookie) ?? throw new InvalidOperationException("The application holds no session cookie.");

        public TestBrowser WithCookie(string cookie)
        {
            TestBrowser browser = ecosystem.NewBrowser();
            browser.SetCookie(backend.Host, SessionCookie, cookie);
            return browser;
        }

        public Task<BrowserResponse> TokenAsync(TestBrowser? browser = null) => (browser ?? AppHttp).NavigateAsync($"{backend.Origin}/auth/token");

        public Task<BrowserResponse> EndSessionAsync(Dictionary<string, string>? headers = null) =>
            AppHttp.NavigateAsync($"{backend.Origin}/auth/native/logout", HttpMethod.Post, headers: headers);

        public async Task<bool> IsSignedInAsync(TestBrowser? browser = null) => (await EcosystemFlow.MeAsync(browser ?? AppHttp, backend)).Authenticated;

        public async Task<bool> AuthorityStillSignedInAsync() =>
            (await SystemBrowser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: "login.test")).Status != HttpStatusCode.Unauthorized;
    }

    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost Backend, SignedInApp App)> StartAsync(TimeSpan? accessTokenLifetime = null, int instances = 1)
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync(options =>
        {
            if (accessTokenLifetime is { } lifetime)
                options.AccessTokenLifetime = lifetime;
        });

        ConsumerHost backend = await ecosystem.AddConsumerAsync(
            AppName,
            instances,
            secret: "secret-app",
            configureServer: configuration => configuration.NativeCallbackSchemes.Add(Scheme),
            configureServices: (services, configuration) => services.AddCloudLoginTokenAuthentication(configuration));

        SignedInApp app = new(ecosystem, backend);
        await app.SignInAsync();
        return (ecosystem, backend, app);
    }

    private static int RefreshCalls(AuthEcosystem ecosystem)
    {
        lock (ecosystem.Network.Requests)
            return ecosystem.Network.Requests.Count(request => request == RefreshEndpoint);
    }

    private static HttpMessageHandler TakeAuthorityOffline(AuthEcosystem ecosystem)
    {
        HttpMessageHandler authority = ecosystem.Network.Routes["login.test"];
        ecosystem.Network.Routes.Remove("login.test");
        return authority;
    }

    // ── Ending the session from the application ──────────────────────────────────────────────

    [Fact]
    public async Task EndingTheSession_FromTheApp_EndsItEverywhere_WithNoBrowser()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        string cookie = app.Cookie;
        Assert.True(await app.AuthorityStillSignedInAsync());
        int browserRequestsBefore = ecosystem.Network.Requests.Count(request => request.Contains("/CloudLogin/Logout", StringComparison.Ordinal));

        BrowserResponse ended = await app.EndSessionAsync();

        Assert.Equal(HttpStatusCode.NoContent, ended.Status);
        Assert.False(await app.IsSignedInAsync());
        Assert.False(await app.IsSignedInAsync(app.WithCookie(cookie)));
        Assert.False(await app.AuthorityStillSignedInAsync());
        Assert.Equal(browserRequestsBefore, ecosystem.Network.Requests.Count(request => request.Contains("/CloudLogin/Logout", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EndingTheSession_ClearsTheCookieTheAppHolds()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        await app.EndSessionAsync();

        Assert.Null(app.AppHttp.CookieValue(backend.Host, SessionCookie));
    }

    [Fact]
    public async Task EndingASessionThatIsAlreadyOver_Answers401_SoTheAppKnowsThereIsNothingLeft()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        Assert.Equal(HttpStatusCode.NoContent, (await app.EndSessionAsync()).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.EndSessionAsync()).Status);
    }

    [Fact]
    public async Task EndingTheSession_WithNoCookieAtAll_Answers401()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        BrowserResponse result = await ecosystem.NewBrowser().NavigateAsync($"{backend.Origin}/auth/native/logout", HttpMethod.Post);

        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.True(await app.IsSignedInAsync());
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task EndingTheSession_FromAWebPage_IsRefused_AndTheSessionSurvives(string fetchSite)
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        BrowserResponse forced = await app.EndSessionAsync(new Dictionary<string, string> { ["Sec-Fetch-Site"] = fetchSite, ["Origin"] = "https://attacker.example" });

        Assert.Equal(HttpStatusCode.Forbidden, forced.Status);
        Assert.True(await app.IsSignedInAsync());
        Assert.True(await app.AuthorityStillSignedInAsync());
    }

    [Fact]
    public async Task WhenTheAuthorityCannotBeReached_NothingChanges_AndTheAppEndsTheSessionOnRetry()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        HttpMessageHandler authority = TakeAuthorityOffline(ecosystem);

        BrowserResponse unavailable = await app.EndSessionAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.Status);
        Assert.True(await app.IsSignedInAsync());

        ecosystem.Network.Routes["login.test"] = authority;

        Assert.Equal(HttpStatusCode.NoContent, (await app.EndSessionAsync()).Status);
        Assert.False(await app.IsSignedInAsync());
        Assert.False(await app.AuthorityStillSignedInAsync());
    }

    [Fact]
    public async Task TheBrowserLogout_ForOlderAppVersions_StillRefusesAnotherSite()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        BrowserResponse forced = await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/logout?redirect_uri={Uri.EscapeDataString($"{Scheme}://auth/callback")}&state={Uri.EscapeDataString(CloudLoginPkce.CreateState())}", initiatorHost: "attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, forced.Status);
        Assert.True(await app.IsSignedInAsync());
    }

    // ── Renewing the session while the person works ──────────────────────────────────────────

    [Fact]
    public async Task ManyRequestsRenewingAtOnce_RefreshOnce_AndTheSessionLives()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync(TimeSpan.FromSeconds(30));
        await using AuthEcosystem owner_ = ecosystem;
        string cookie = app.Cookie;
        int before = RefreshCalls(ecosystem);

        TestBrowser[] parallel = [.. Enumerable.Range(0, 8).Select(_ => app.WithCookie(cookie))];
        BrowserResponse[] results = await Task.WhenAll(parallel.Select(browser => app.TokenAsync(browser)));

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));
        Assert.Equal(1, RefreshCalls(ecosystem) - before);
        Assert.True(await app.IsSignedInAsync(parallel[^1]));
        Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync(parallel[0])).Status);
        Assert.True(await app.AuthorityStillSignedInAsync());
    }

    [Fact]
    public async Task ARequestCarryingACookieAnotherRequestAlreadyRenewed_IsGivenThatRenewal_InsteadOfEndingTheSession()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync(TimeSpan.FromSeconds(30));
        await using AuthEcosystem owner_ = ecosystem;
        string stale = app.Cookie;

        Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync()).Status);
        int afterFirstRenewal = RefreshCalls(ecosystem);
        Assert.NotEqual(stale, app.Cookie);

        BrowserResponse replayed = await app.TokenAsync(app.WithCookie(stale));

        Assert.Equal(HttpStatusCode.OK, replayed.Status);
        Assert.Equal(afterFirstRenewal, RefreshCalls(ecosystem));
        Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync()).Status);
        Assert.True(await app.IsSignedInAsync());
        Assert.True(await app.AuthorityStillSignedInAsync());
    }

    [Fact]
    public async Task ARenewalSharedAcrossInstances_IsNotRepeatedByTheOtherInstance()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync(TimeSpan.FromSeconds(30), instances: 2);
        await using AuthEcosystem owner_ = ecosystem;
        string stale = app.Cookie;

        backend.Balancer.Instance = 0;
        Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync()).Status);
        int afterFirstRenewal = RefreshCalls(ecosystem);

        backend.Balancer.Instance = 1;
        Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync(app.WithCookie(stale))).Status);

        Assert.Equal(afterFirstRenewal, RefreshCalls(ecosystem));
        Assert.True(await app.AuthorityStillSignedInAsync());
    }

    [Fact]
    public async Task WhenTheAuthorityCannotBeReachedForARenewal_TheSessionIsKept_AndRenewsLater()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync(TimeSpan.FromSeconds(2));
        await using AuthEcosystem owner_ = ecosystem;
        await Task.Delay(TimeSpan.FromSeconds(3));
        HttpMessageHandler authority = TakeAuthorityOffline(ecosystem);

        BrowserResponse unavailable = await app.TokenAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.Status);
        Assert.True(await app.IsSignedInAsync());

        ecosystem.Network.Routes["login.test"] = authority;

        Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync()).Status);
        Assert.True(await app.IsSignedInAsync());
    }

    [Fact]
    public async Task ARenewalTheAuthorityRefuses_EndsTheSession()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync(TimeSpan.FromSeconds(2));
        await using AuthEcosystem owner_ = ecosystem;
        (_, string? sessionId) = await EcosystemFlow.MeAsync(app.AppHttp, backend);
        await ecosystem.Authority.SessionService.RevokeSessionAsync(sessionId!, Server.Core.Domain.SessionRevocationReasons.UserSignedOut);
        await Task.Delay(TimeSpan.FromSeconds(3));

        BrowserResponse refused = await app.TokenAsync();

        Assert.Contains(refused.Status, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.False(await app.IsSignedInAsync());
    }

    [Fact]
    public async Task AFreshToken_IsServedWithoutTouchingTheAuthority()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, SignedInApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        int before = RefreshCalls(ecosystem);

        for (int request = 0; request < 5; request++)
            Assert.Equal(HttpStatusCode.OK, (await app.TokenAsync()).Status);

        Assert.Equal(before, RefreshCalls(ecosystem));
    }
}
