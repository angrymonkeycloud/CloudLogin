using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

public class LogoutTests
{
    private const string AuthorityHostName = "login.test";

    private static async Task<bool> AuthoritySignedInAsync(TestBrowser browser) =>
        (await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: AuthorityHostName)).Status == HttpStatusCode.OK;

    [Fact]
    public async Task AnApplicationWhoseHostnameIsInNoAllowlist_CompletesALogoutThroughTheAuthority()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("preview-4821-app");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        Assert.Empty(ecosystem.Authority.Services.GetService(typeof(Server.CloudLoginWebConfiguration)) is Server.CloudLoginWebConfiguration configuration ? configuration.AllowedRedirectOrigins : ["missing"]);
        Assert.True(await AuthoritySignedInAsync(browser));

        BrowserResponse result = await browser.NavigateAsync($"{app.Origin}/auth/logout?returnUrl=/goodbye", initiatorHost: app.Host);

        Assert.Equal("/goodbye", result.FinalUrl.AbsolutePath);
        Assert.Equal(app.Host, result.FinalUrl.Host);
        Assert.Contains(result.Chain, url => url.Host == AuthorityHostName && url.AbsolutePath == "/CloudLogin/Logout");
        Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
        Assert.False(await AuthoritySignedInAsync(browser));
        Assert.All(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>(), family => Assert.True(family.IsRevoked));
    }

    [Fact]
    public async Task LogoutWorksForEveryHostnameShape_WithoutRegisteringAnyOfThem()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();

        foreach (string name in new[] { "localhost-style", "pr-17-preview", "staging-slot", "production" })
        {
            ConsumerHost app = await ecosystem.AddConsumerAsync(name);
            TestBrowser browser = ecosystem.NewBrowser();
            await EcosystemFlow.LoginAsync(browser, app);

            BrowserResponse result = await browser.NavigateAsync($"{app.Origin}/auth/logout?returnUrl=/", initiatorHost: app.Host);

            Assert.Equal(app.Host, result.FinalUrl.Host);
            Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
            Assert.False(await AuthoritySignedInAsync(browser));
        }
    }

    [Fact]
    public async Task AnInvalidReturnDestination_NeverPreventsTheSessionEnding_AndLandsOnASafeLocalPage()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        BrowserResponse result = await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer={Uri.EscapeDataString("https://evil.test/phish")}", initiatorHost: AuthorityHostName);

        Assert.Equal(AuthorityHostName, result.FinalUrl.Host);
        Assert.NotEqual("evil.test", result.FinalUrl.Host);
        Assert.False(await AuthoritySignedInAsync(browser));
    }

    private static async Task<string> OpenLogoutAsync(AuthEcosystem ecosystem, ConsumerHost app, string secret = "secret-a")
    {
        ApiResult begun = await new AuthorityClient(ecosystem.Network).PostAsync("/CloudLogin/Authorize/Logout", new { postLogoutRedirectUri = $"{app.Origin}/bye" }, AuthorityClient.Basic(app.Name, secret));
        Assert.Equal(HttpStatusCode.OK, begun.Status);
        return begun.Property("logoutUrl")!;
    }

    [Fact]
    public async Task AFabricatedLogoutReference_FromAnotherSite_DoesNotEndTheSession()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        BrowserResponse forged = await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer=cltx:{Guid.NewGuid():N}", initiatorHost: "attacker.example");

        Assert.Equal(AuthorityHostName, forged.FinalUrl.Host);
        Assert.True(await AuthoritySignedInAsync(browser));
        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
    }

    [Theory]
    [InlineData("cltx:00000000000000000000000000000000")]
    [InlineData("cltx:not-a-guid")]
    [InlineData("cltx:")]
    public async Task MalformedOrEmptyLogoutReferences_FromAnotherSite_AuthorizeNothing(string reference)
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer={Uri.EscapeDataString(reference)}", initiatorHost: "attacker.example");

        Assert.True(await AuthoritySignedInAsync(browser));
    }

    [Fact]
    public async Task ALogoutReference_IsSingleUse_AndAReplayFromAnotherSiteEndsNothing()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TestBrowser first = ecosystem.NewBrowser();
        TestBrowser second = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(first);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(second);
        string logoutUrl = await OpenLogoutAsync(ecosystem, app);

        BrowserResponse used = await first.NavigateAsync(logoutUrl, initiatorHost: app.Host);
        Assert.Equal("/bye", used.FinalUrl.AbsolutePath);
        Assert.False(await AuthoritySignedInAsync(first));

        BrowserResponse replay = await second.NavigateAsync(logoutUrl, initiatorHost: app.Host);
        Assert.NotEqual("/bye", replay.FinalUrl.AbsolutePath);
        Assert.True(await AuthoritySignedInAsync(second));
    }

    [Fact]
    public async Task AnExpiredLogoutReference_AuthorizesNothing()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        string logoutUrl = await OpenLogoutAsync(ecosystem, app);

        foreach (LoginRequestDocument document in ecosystem.Authority.LoginRequests.Documents.Values.Where(item => item.Kind == LoginRequestKinds.Logout))
            document.ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1);

        BrowserResponse result = await browser.NavigateAsync(logoutUrl, initiatorHost: app.Host);

        Assert.NotEqual("/bye", result.FinalUrl.AbsolutePath);
        Assert.True(await AuthoritySignedInAsync(browser));
    }

    [Fact]
    public async Task ASignInReference_CannotBeUsedToAuthorizeALogout()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        ApiResult begun = await new AuthorityClient(ecosystem.Network).BeginAsync(AuthorityClient.Basic("app-a", "secret-a"), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()));

        await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer={Uri.EscapeDataString(begun.Property("transaction")!)}", initiatorHost: app.Host);

        Assert.True(await AuthoritySignedInAsync(browser));
    }

    [Fact]
    public async Task AnInvalidReference_AuthorizesNothing_ButTheSameOriginSignOutButtonStillWorks()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        BrowserResponse result = await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer=cltx:{Guid.NewGuid():N}", initiatorHost: AuthorityHostName);

        Assert.Equal(AuthorityHostName, result.FinalUrl.Host);
        Assert.Equal("/", result.FinalUrl.AbsolutePath);
        Assert.False(await AuthoritySignedInAsync(browser));
    }

    [Fact]
    public async Task ABrowserThatSendsNoFetchMetadata_IsJudgedByItsReferrer()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout", headers: new Dictionary<string, string> { ["Sec-Fetch-Site"] = "", ["Referer"] = "https://attacker.example/page" });
        Assert.True(await AuthoritySignedInAsync(browser));

        await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout", headers: new Dictionary<string, string> { ["Sec-Fetch-Site"] = "", ["Referer"] = $"{AuthorityHost.Origin}/Account" });
        Assert.False(await AuthoritySignedInAsync(browser));
    }

    [Theory]
    [InlineData("http://app-a.test/after")]
    [InlineData("javascript:alert(1)")]
    public async Task ALogoutTransactionCannotNameAnUnsafeDestination(string destination)
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");

        ApiResult begun = await new AuthorityClient(ecosystem.Network).PostAsync("/CloudLogin/Authorize/Logout", new { postLogoutRedirectUri = destination }, AuthorityClient.Basic("app-a", "secret-a"));

        Assert.Equal(HttpStatusCode.BadRequest, begun.Status);
    }

    [Fact]
    public async Task OnlyAnAuthenticatedApplication_CanOpenALogoutTransaction()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/CloudLogin/Authorize/Logout", new { postLogoutRedirectUri = "https://app-a.test/bye" })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/CloudLogin/Authorize/Logout", new { postLogoutRedirectUri = "https://app-a.test/bye" }, AuthorityClient.Basic("app-a", "wrong"))).Status);
    }

    [Fact]
    public async Task ACrossSiteRequestWithNoLogoutTransaction_CannotLogTheVictimOut()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        BrowserResponse forced = await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout", initiatorHost: "attacker.example");

        Assert.True(await AuthoritySignedInAsync(browser));
        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
        Assert.Equal(AuthorityHostName, forced.FinalUrl.Host);
    }

    [Fact]
    public async Task AConsumerLogoutStartedFromAnotherSite_IsRefused_AndTheSessionSurvives()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        BrowserResponse forced = await browser.NavigateAsync($"{app.Origin}/auth/logout?returnUrl=/", initiatorHost: "attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, forced.Status);
        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
        Assert.True(await AuthoritySignedInAsync(browser));
    }

    [Fact]
    public async Task WhenTheAuthorityRefusesTheLogoutTransaction_TheLocalSessionStillEnds()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.LoginAsync(browser, app);

        await ecosystem.BlockAsync(app);
        BrowserResponse result = await browser.NavigateAsync($"{app.Origin}/auth/logout?returnUrl=/goodbye", initiatorHost: app.Host);

        Assert.Equal("/goodbye", result.FinalUrl.AbsolutePath);
        Assert.Equal(app.Host, result.FinalUrl.Host);
        Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
    }

    [Fact]
    public async Task TheStaticAllowlistStillWorks_AndIsNeverReplacedByAnUnrestrictedRedirect()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        ((Server.CloudLoginWebConfiguration)ecosystem.Authority.Services.GetService(typeof(Server.CloudLoginWebConfiguration))!).AllowedRedirectOrigins.Add("https://legacy.test");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        ecosystem.Network.Routes["legacy.test"] = ecosystem.Network.Routes[app.Host];

        BrowserResponse listed = await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer={Uri.EscapeDataString("https://legacy.test/after")}", initiatorHost: AuthorityHostName, maxRedirects: 1);
        BrowserResponse unlisted = await ecosystem.NewBrowser().NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout?referer={Uri.EscapeDataString("https://other.test/after")}", initiatorHost: AuthorityHostName, maxRedirects: 1);

        Assert.Equal("legacy.test", listed.Chain[1].Host);
        Assert.DoesNotContain(unlisted.Chain, url => url.Host == "other.test");
    }
}
