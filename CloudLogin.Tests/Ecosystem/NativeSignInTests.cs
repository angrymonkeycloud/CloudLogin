using System.Net;
using System.Text.Json;
using AngryMonkey.CloudLogin.Server.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>
/// A native application signing in through its own backend: the system browser runs the website flow, and the app receives the session as a
/// handoff only it can redeem. The app is a separate HTTP stack with its own cookie container, as a phone's is.
/// </summary>
public class NativeSignInTests
{
    private const string Scheme = "blusky";

    private sealed class NativeApp(AuthEcosystem ecosystem, ConsumerHost backend)
    {
        public TestBrowser AppHttp { get; } = ecosystem.NewBrowser();
        public TestBrowser SystemBrowser { get; } = ecosystem.NewBrowser();
        public string Verifier { get; private set; } = CloudLoginPkce.CreateVerifier();
        public string State { get; private set; } = CloudLoginPkce.CreateState();
        public string Callback => $"{Scheme}://auth/callback";

        public string LoginUrl(string? challenge = null, string? state = null, string? redirect = null) =>
            $"{backend.Origin}/auth/native/login?challenge={Uri.EscapeDataString(challenge ?? CloudLoginPkce.CreateChallenge(Verifier))}&state={Uri.EscapeDataString(state ?? State)}&redirect_uri={Uri.EscapeDataString(redirect ?? Callback)}";

        public async Task<BrowserResponse> SignInInTheSystemBrowserAsync(TestBrowser? browser = null)
        {
            browser ??= SystemBrowser;
            BrowserResponse start = await browser.NavigateAsync(LoginUrl());
            string reference = start.Query("referer") ?? throw new InvalidOperationException($"The backend did not send the browser to the authority: {start.Status} {start.FinalUrl} {start.Body}");

            return await EcosystemFlow.SignInAtAuthorityAsync(browser, reference);
        }

        public async Task<(string Handoff, string State)> ReceiveHandoffAsync(TestBrowser? browser = null)
        {
            BrowserResponse handed = await SignInInTheSystemBrowserAsync(browser);

            Assert.Equal(Scheme, handed.FinalUrl.Scheme);
            System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(handed.FinalUrl.Query);
            return (query["handoff"]!, query["state"]!);
        }

        public Task<BrowserResponse> ExchangeAsync(string handoff, string? verifier = null) =>
            AppHttp.NavigateAsync($"{backend.Origin}/auth/native/exchange", HttpMethod.Post, json: JsonSerializer.Serialize(new NativeExchangeRequest(handoff, verifier ?? Verifier)));

        public async Task<bool> IsSignedInAsync() => (await EcosystemFlow.MeAsync(AppHttp, backend)).Authenticated;
    }

    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost Backend, NativeApp App)> StartAsync(int instances = 1, params string[] schemes)
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost backend = await ecosystem.AddConsumerAsync("blusky-app", instances, secret: "secret-app", configureServer: configuration => configuration.NativeCallbackSchemes.AddRange(schemes.Length == 0 ? [Scheme] : schemes));
        return (ecosystem, backend, new NativeApp(ecosystem, backend));
    }

    [Fact]
    public async Task ANativeApplication_SignsIn_ThroughItsBackend_AndReceivesTheSessionNotTheBrowser()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        (string handoff, string state) = await app.ReceiveHandoffAsync();

        Assert.Equal(app.State, state);
        Assert.False(await app.IsSignedInAsync());
        Assert.DoesNotContain(app.SystemBrowser.Cookies, cookie => cookie.Host == backend.Host && cookie.Name == "blusky-app.session");

        BrowserResponse exchanged = await app.ExchangeAsync(handoff);

        Assert.Equal(HttpStatusCode.OK, exchanged.Status);
        Assert.Contains(AuthEcosystem.AuthorityEmail, exchanged.Body, StringComparison.OrdinalIgnoreCase);
        Assert.True(await app.IsSignedInAsync());
        Assert.DoesNotContain(app.SystemBrowser.Cookies, cookie => cookie.Host == backend.Host && cookie.Name == "blusky-app.session");
    }

    [Fact]
    public async Task TheApplicationsCookie_IsPersistent_AndHttpOnly()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        (string handoff, _) = await app.ReceiveHandoffAsync();

        await app.ExchangeAsync(handoff);

        Assert.Contains(app.AppHttp.Cookies, item => item.Host == backend.Host && item.Name == "blusky-app.session");
        DateTimeOffset? expiresOn = app.AppHttp.CookieExpiresOn(backend.Host, "blusky-app.session");
        Assert.True(expiresOn > DateTimeOffset.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task AHandoff_IsSingleUse()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        (string handoff, _) = await app.ReceiveHandoffAsync();

        Assert.Equal(HttpStatusCode.OK, (await app.ExchangeAsync(handoff)).Status);
        TestBrowser other = ecosystem.NewBrowser();
        BrowserResponse replay = await other.NavigateAsync($"{backend.Origin}/auth/native/exchange", HttpMethod.Post, json: JsonSerializer.Serialize(new NativeExchangeRequest(handoff, app.Verifier)));

        Assert.Equal(HttpStatusCode.BadRequest, replay.Status);
    }

    [Fact]
    public async Task AnInterceptedHandoff_IsUselessWithoutTheVerifier_AndIsBurnedByTheAttempt()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        (string handoff, _) = await app.ReceiveHandoffAsync();
        TestBrowser attacker = ecosystem.NewBrowser();

        BrowserResponse stolen = await attacker.NavigateAsync($"{backend.Origin}/auth/native/exchange", HttpMethod.Post, json: JsonSerializer.Serialize(new NativeExchangeRequest(handoff, CloudLoginPkce.CreateVerifier())));
        Assert.Equal(HttpStatusCode.BadRequest, stolen.Status);
        Assert.False((await EcosystemFlow.MeAsync(attacker, backend)).Authenticated);

        Assert.Equal(HttpStatusCode.BadRequest, (await app.ExchangeAsync(handoff)).Status);
        Assert.False(await app.IsSignedInAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-handoff")]
    public async Task AnUnknownOrEmptyHandoff_IsRefused(string handoff)
    {
        (AuthEcosystem ecosystem, _, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        Assert.Equal(HttpStatusCode.BadRequest, (await app.ExchangeAsync(handoff)).Status);
        Assert.False(await app.IsSignedInAsync());
    }

    [Theory]
    [InlineData("https://evil.test/callback")]
    [InlineData("http://auth/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("otherapp://auth/callback")]
    [InlineData("blusky://auth/callback?next=https://evil.test")]
    [InlineData("blusky://auth/callback#fragment")]
    [InlineData("blusky://user:pass@auth/callback")]
    [InlineData("blusky://auth/callback\\evil")]
    [InlineData("")]
    public async Task OnlyAConfiguredCustomSchemeWithNoDecoration_CanReceiveTheHandoff(string redirect)
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        BrowserResponse response = await app.SystemBrowser.NavigateAsync(app.LoginUrl(redirect: redirect));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Null(response.Query("referer"));
        Assert.Equal(backend.Host, response.FinalUrl.Host);
    }

    [Fact]
    public async Task WithNoNativeSchemeConfigured_TheNativeEndpointsAreClosed()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost backend = await ecosystem.AddConsumerAsync("plain-app", secret: "secret-plain");
        NativeApp app = new(ecosystem, backend);

        Assert.Equal(HttpStatusCode.BadRequest, (await app.SystemBrowser.NavigateAsync(app.LoginUrl())).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/logout?redirect_uri={Uri.EscapeDataString(app.Callback)}&state={app.State}")).Status);
    }

    [Theory]
    [InlineData("short", true)]
    [InlineData("has spaces in the state value....", false)]
    [InlineData("state-with-bad-chars-<script>-0123456789", false)]
    public async Task ABadStateOrChallenge_IsRefused(string value, bool useAsState)
    {
        (AuthEcosystem ecosystem, _, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        string url = useAsState ? app.LoginUrl(state: value) : app.LoginUrl(state: CloudLoginPkce.CreateState(), challenge: value);
        BrowserResponse response = await app.SystemBrowser.NavigateAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Null(response.Query("referer"));
    }

    [Fact]
    public async Task ACallbackReachingADifferentBrowser_ProducesNoHandoff()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        BrowserResponse start = await app.SystemBrowser.NavigateAsync(app.LoginUrl());
        string reference = start.Query("referer")!;
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(app.SystemBrowser);
        string callback = await EcosystemFlow.CallbackUrlAsync(app.SystemBrowser, reference);
        TestBrowser victim = ecosystem.NewBrowser();

        BrowserResponse injected = await victim.NavigateAsync(callback);

        Assert.NotEqual(Scheme, injected.FinalUrl.Scheme);
        Assert.Contains("error=invalid_callback", injected.FinalUrl.Query);
        Assert.False((await EcosystemFlow.MeAsync(victim, backend)).Authenticated);
    }

    [Fact]
    public async Task AnAuthorityError_ReturnsToTheApplicationWithItsState_AndNoSession()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        BrowserResponse start = await app.SystemBrowser.NavigateAsync(app.LoginUrl());
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(app.SystemBrowser);
        (_, string state) = EcosystemFlow.CodeAndState(await EcosystemFlow.CallbackUrlAsync(app.SystemBrowser, start.Query("referer")!));

        BrowserResponse failed = await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/callback?error=access_denied&state={Uri.EscapeDataString(state)}");

        Assert.Equal(Scheme, failed.FinalUrl.Scheme);
        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(failed.FinalUrl.Query);
        Assert.Equal("login_failed", query["error"]);
        Assert.Equal(app.State, query["state"]);
        Assert.Null(query["handoff"]);
    }

    [Fact]
    public async Task AHandoffMadeByOneInstance_IsRedeemedByAnother()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync(instances: 3);
        await using AuthEcosystem owner_ = ecosystem;

        backend.Balancer.Instance = 0;
        (string handoff, _) = await app.ReceiveHandoffAsync();

        backend.Balancer.Instance = 2;
        BrowserResponse exchanged = await app.ExchangeAsync(handoff);
        Assert.Equal(HttpStatusCode.OK, exchanged.Status);

        backend.Balancer.Instance = 1;
        Assert.True(await app.IsSignedInAsync());
    }

    [Fact]
    public async Task TheNativeSession_IsEndedByASignOutAnywhere_ThroughTheBackChannel()
    {
        (AuthEcosystem ecosystem, _, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        (string handoff, _) = await app.ReceiveHandoffAsync();
        await app.ExchangeAsync(handoff);
        Assert.True(await app.IsSignedInAsync());

        await ecosystem.Authority.SessionService.RevokeAllForUserAsync(ecosystem.User.Id, Server.Core.Domain.SessionRevocationReasons.AdminRevoked);

        Assert.False(await app.IsSignedInAsync());
    }

    // ── Logout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANativeLogout_EndsTheAuthoritySession_ReturnsToTheApp_AndEndsTheAppsSessionToo()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        (string handoff, _) = await app.ReceiveHandoffAsync();
        await app.ExchangeAsync(handoff);
        Assert.True(await app.IsSignedInAsync());

        BrowserResponse result = await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/logout?redirect_uri={Uri.EscapeDataString(app.Callback)}&state={Uri.EscapeDataString(app.State)}");

        Assert.Equal(Scheme, result.FinalUrl.Scheme);
        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(result.FinalUrl.Query);
        Assert.Equal("logout", query["operation"]);
        Assert.Equal(app.State, query["state"]);
        Assert.Null(query["error"]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.SystemBrowser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: "login.test")).Status);
        Assert.False(await app.IsSignedInAsync());
    }

    [Fact]
    public async Task ANativeLogout_StartedFromAnotherSite_IsRefused_AndTheSessionSurvives()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        (string handoff, _) = await app.ReceiveHandoffAsync();
        await app.ExchangeAsync(handoff);

        BrowserResponse forced = await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/logout?redirect_uri={Uri.EscapeDataString(app.Callback)}&state={Uri.EscapeDataString(app.State)}", initiatorHost: "attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, forced.Status);
        Assert.Equal(HttpStatusCode.OK, (await app.SystemBrowser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: "login.test")).Status);
        Assert.True(await app.IsSignedInAsync());
    }

    [Fact]
    public async Task ALoggedOutCallback_WithStateThisBackendNeverIssued_GoesNowhere()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        BrowserResponse forged = await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/logged-out?state=forged-state-value");

        Assert.NotEqual(Scheme, forged.FinalUrl.Scheme);
        Assert.Equal(backend.Host, forged.FinalUrl.Host);
    }

    [Fact]
    public async Task WhenTheAuthorityRefusesTheLogout_TheAppIsToldAndNothingIsEndedThere()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend, NativeApp app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        await app.SystemBrowser.NavigateAsync(app.LoginUrl());
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(app.SystemBrowser);
        await ecosystem.BlockAsync(backend);

        BrowserResponse result = await app.SystemBrowser.NavigateAsync($"{backend.Origin}/auth/native/logout?redirect_uri={Uri.EscapeDataString(app.Callback)}&state={Uri.EscapeDataString(app.State)}");

        Assert.Equal(Scheme, result.FinalUrl.Scheme);
        Assert.Equal("logout_failed", System.Web.HttpUtility.ParseQueryString(result.FinalUrl.Query)["error"]);
    }
}
