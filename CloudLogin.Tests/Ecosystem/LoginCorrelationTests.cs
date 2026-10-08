using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

public class LoginCorrelationTests
{
    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost App)> StartAsync(string name = "app-a", TimeSpan? lifetime = null)
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync(name, configureConsumer: settings =>
        {
            if (lifetime is { } value)
                settings["CloudLogin:LoginTransactionLifetime"] = value.ToString();
        });
        return (ecosystem, app);
    }

    [Fact]
    public async Task ACallbackProducedByAnAttackersLogin_CannotSignADifferentBrowserIn()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        TestBrowser attacker = ecosystem.NewBrowser();
        TestBrowser victim = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(attacker, app);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(attacker);
        string attackerCallback = await EcosystemFlow.CallbackUrlAsync(attacker, reference);

        BrowserResponse delivered = await victim.NavigateAsync(attackerCallback, initiatorHost: "attacker.example");

        Assert.Equal("/", delivered.FinalUrl.AbsolutePath);
        Assert.Equal("invalid_callback", delivered.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(victim, app)).Authenticated);

        // The attacker's own browser can still finish its own sign-in: nothing about it was spent by the attempt.
        BrowserResponse own = await attacker.NavigateAsync(attackerCallback);
        Assert.True((await EcosystemFlow.MeAsync(attacker, app)).Authenticated, $"{own.Status} {own.FinalUrl}");
    }

    [Fact]
    public async Task AVictimWhoStartedTheirOwnLogin_StillRejectsTheAttackersCallback()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        TestBrowser attacker = ecosystem.NewBrowser();
        TestBrowser victim = ecosystem.NewBrowser();

        (_, string attackerReference) = await EcosystemFlow.StartLoginAsync(attacker, app);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(attacker);
        string attackerCallback = await EcosystemFlow.CallbackUrlAsync(attacker, attackerReference);

        await EcosystemFlow.StartLoginAsync(victim, app);
        BrowserResponse delivered = await victim.NavigateAsync(attackerCallback);

        Assert.Equal("invalid_callback", delivered.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(victim, app)).Authenticated);
    }

    [Fact]
    public async Task TheVictimsStateWithTheAttackersCode_IsRefusedByThePkceBinding()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        TestBrowser attacker = ecosystem.NewBrowser();
        TestBrowser victim = ecosystem.NewBrowser();

        (_, string attackerReference) = await EcosystemFlow.StartLoginAsync(attacker, app);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(attacker);
        (string attackerCode, _) = EcosystemFlow.CodeAndState(await EcosystemFlow.CallbackUrlAsync(attacker, attackerReference));

        BrowserResponse victimStart = await victim.NavigateAsync($"{app.Origin}/auth/login?returnUrl=/home", follow: true);
        string victimState = ecosystem.Authority.LoginRequests.Documents.Values.Last(document => document.Kind == LoginRequestKinds.Authorization).ClientState!;

        BrowserResponse forged = await victim.NavigateAsync($"{app.Origin}/auth/callback?code={attackerCode}&state={Uri.EscapeDataString(victimState)}");

        Assert.Equal("user_not_found", forged.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(victim, app)).Authenticated);
        Assert.Equal(HttpStatusCode.NotFound, victimStart.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("not-a-state-this-browser-ever-created-1234567890")]
    public async Task MissingMalformedOrUnknownState_IsRejected(string state)
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = ecosystem.NewBrowser();

        BrowserResponse response = await browser.NavigateAsync($"{app.Origin}/auth/callback?code={Guid.NewGuid()}&state={Uri.EscapeDataString(state)}");

        Assert.Equal("invalid_callback", response.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
    }

    [Fact]
    public async Task ATamperedCorrelationCookie_IsRejected()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        string callback = await EcosystemFlow.CallbackUrlAsync(browser, reference);

        (string host, string name, string value) = browser.Cookies.Single(cookie => cookie.Name.Contains(".tx."));
        browser.SetCookie(host, name, value[..^4] + "AAAA");

        BrowserResponse response = await browser.NavigateAsync(callback);

        Assert.Equal("invalid_callback", response.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
    }

    [Fact]
    public async Task AnExpiredCorrelation_IsRejected()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync(lifetime: TimeSpan.FromSeconds(1));
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        string callback = await EcosystemFlow.CallbackUrlAsync(browser, reference);

        string? cookieValue = browser.CookieValue(app.Host, ".tx.");
        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        browser.SetCookie(app.Host, browser.Cookies.Single(cookie => cookie.Name.Contains(".tx.")).Name, cookieValue!);

        BrowserResponse response = await browser.NavigateAsync(callback);

        Assert.Equal("invalid_callback", response.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
    }

    [Fact]
    public async Task ACallbackCannotBeReplayed_EvenWithTheCorrelationCookieRestored()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        string callback = await EcosystemFlow.CallbackUrlAsync(browser, reference);
        (string host, string name, string value) = browser.Cookies.Single(cookie => cookie.Name.Contains(".tx."));

        BrowserResponse first = await browser.NavigateAsync(callback);
        Assert.Equal("/home", first.FinalUrl.AbsolutePath);
        Assert.Equal(0, browser.CookieCount(app.Host, ".tx."));

        TestBrowser other = ecosystem.NewBrowser();
        BrowserResponse replayWithoutCookie = await other.NavigateAsync(callback);
        Assert.Equal("invalid_callback", replayWithoutCookie.Query("error"));

        other.SetCookie(host, name, value);
        BrowserResponse replayWithCookie = await other.NavigateAsync(callback);
        Assert.Equal("user_not_found", replayWithCookie.Query("error"));
        Assert.False((await EcosystemFlow.MeAsync(other, app)).Authenticated);
    }

    [Fact]
    public async Task WhenTheAuthorityRefusesTheTransaction_NoSignInIsAttemptedAndNothingFallsBackToAPlainUrl()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = ecosystem.NewBrowser();

        await ecosystem.BlockAsync(app);

        BrowserResponse response = await browser.NavigateAsync($"{app.Origin}/auth/login?returnUrl=/home", follow: false);

        Assert.Equal(HttpStatusCode.BadGateway, response.Status);
        Assert.Null(response.Query("referer"));
        Assert.Equal(0, browser.CookieCount(app.Host, ".tx."));
    }

    [Fact]
    public async Task TheCallbackIsBuiltFromConfiguration_NotFromTheHostHeader()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        ecosystem.Network.Routes["evil.test"] = ecosystem.Network.Routes[app.Host];
        TestBrowser browser = ecosystem.NewBrowser();

        await browser.NavigateAsync("https://evil.test/auth/login?returnUrl=/home");

        LoginRequestDocument transaction = ecosystem.Authority.LoginRequests.Documents.Values.Single(document => document.Kind == LoginRequestKinds.Authorization);
        Assert.Equal($"{app.Origin}/auth/callback", transaction.ReturnUrl);
    }

    [Fact]
    public async Task WithoutAConfiguredPublicUrl_ANonLoopbackHostCannotStartASignIn()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", configureConsumer: settings => settings.Remove("CloudLogin:PublicUrl"), publicUrl: "");
        TestBrowser browser = ecosystem.NewBrowser();

        BrowserResponse response = await browser.NavigateAsync($"{app.Origin}/auth/login", follow: false);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Empty(ecosystem.Authority.LoginRequests.Documents.Values.Where(document => document.Kind == LoginRequestKinds.Authorization));
    }

    [Theory]
    [InlineData("http://app-a.test/auth/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("/relative/callback")]
    public async Task ATransactionCannotNameAnUnsafeReturnUrl(string returnUrl)
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);

        ApiResult result = await client.BeginAsync(AuthorityClient.Basic("app-a", "secret-a"), returnUrl, CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Contains(ecosystem.Authority.Audit.Events, item => item.EventType == "Transaction.Rejected" && item.ClientId == "app-a");
        _ = app;
    }

    [Theory]
    [InlineData("https://app-a.test/somewhere-else")]
    [InlineData("https://preview-7.app-a.test/auth/callback")]
    public async Task ABackend_MayReturnToAnyHttpsAddress_BecauseItsKeyIsWhatIsTrusted(string returnUrl)
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");

        ApiResult result = await new AuthorityClient(ecosystem.Network).BeginAsync(AuthorityClient.Basic("app-a", app.Secret), returnUrl, CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Contains(new Uri(returnUrl).GetLeftPart(UriPartial.Authority), ecosystem.Authority.Applications.Documents["app-a"].Origins);
    }

    [Fact]
    public async Task ATransactionNeedsAStateAndAPkceChallenge()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);
        string authorization = AuthorityClient.Basic("app-a", "secret-a");
        string challenge = CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.BeginAsync(authorization, "https://app-a.test/auth/callback", string.Empty, challenge)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.BeginAsync(authorization, "https://app-a.test/auth/callback", "short", challenge)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.BeginAsync(authorization, "https://app-a.test/auth/callback", CloudLoginPkce.CreateState(), string.Empty)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.BeginAsync(authorization, "https://app-a.test/auth/callback", CloudLoginPkce.CreateState(), "tooshort")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.BeginAsync(authorization, "https://app-a.test/auth/callback", CloudLoginPkce.CreateState(), challenge, method: "plain")).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.BeginAsync(authorization, "https://app-a.test/auth/callback", CloudLoginPkce.CreateState(), challenge)).Status);
    }

    private static async Task<(string Code, string Verifier, string Redirect)> IssueCodeAsync(AuthEcosystem ecosystem, ConsumerHost app, string secret)
    {
        AuthorityClient client = new(ecosystem.Network);
        string verifier = CloudLoginPkce.CreateVerifier();
        string redirect = $"{app.Origin}/auth/callback";

        ApiResult begun = await client.BeginAsync(AuthorityClient.Basic(app.Name, secret), redirect, CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier));
        Assert.Equal(HttpStatusCode.OK, begun.Status);

        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        (string code, _) = EcosystemFlow.CodeAndState(await EcosystemFlow.CallbackUrlAsync(browser, begun.Property("transaction")!));

        return (code, verifier, redirect);
    }

    [Fact]
    public async Task ACodeIsBoundToTheClientThatStartedTheTransaction_AndSurvivesAWrongClientsAttempt()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost owner = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        ConsumerHost rival = await ecosystem.AddConsumerAsync("app-b", secret: "secret-b");
        AuthorityClient client = new(ecosystem.Network);
        (string code, string verifier, string redirect) = await IssueCodeAsync(ecosystem, owner, "secret-a");

        ApiResult stolen = await client.RedeemAsync(AuthorityClient.Basic("app-b", "secret-b"), code, verifier, redirect);
        Assert.Equal(HttpStatusCode.BadRequest, stolen.Status);

        ApiResult redeemed = await client.RedeemAsync(AuthorityClient.Basic("app-a", "secret-a"), code, verifier, redirect);
        Assert.Equal(HttpStatusCode.OK, redeemed.Status);
        _ = rival;
    }

    [Fact]
    public async Task ACodeRedeemedWithTheWrongVerifier_IsBurned_SoTheRightOneNoLongerWorks()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);
        (string code, string verifier, string redirect) = await IssueCodeAsync(ecosystem, app, "secret-a");
        string authorization = AuthorityClient.Basic("app-a", "secret-a");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.RedeemAsync(authorization, code, CloudLoginPkce.CreateVerifier(), redirect)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.RedeemAsync(authorization, code, verifier, redirect)).Status);
    }

    [Fact]
    public async Task ACodeRedeemedForADifferentRedirectUri_IsRefusedAndBurned()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);
        (string code, string verifier, string redirect) = await IssueCodeAsync(ecosystem, app, "secret-a");
        string authorization = AuthorityClient.Basic("app-a", "secret-a");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.RedeemAsync(authorization, code, verifier, "https://app-a.test/other")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.RedeemAsync(authorization, code, verifier, redirect)).Status);
    }

    [Fact]
    public async Task ACodeCanBeRedeemedOnlyOnce()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);
        (string code, string verifier, string redirect) = await IssueCodeAsync(ecosystem, app, "secret-a");
        string authorization = AuthorityClient.Basic("app-a", "secret-a");

        Assert.Equal(HttpStatusCode.OK, (await client.RedeemAsync(authorization, code, verifier, redirect)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.RedeemAsync(authorization, code, verifier, redirect)).Status);
    }

    [Fact]
    public async Task TheLegacyHandoffEndpoint_RefusesATransactionBoundRequest_EvenWhenCompatibilityIsOn()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        AuthorityClient client = new(ecosystem.Network);
        (string code, _, _) = await IssueCodeAsync(ecosystem, app, "secret-a");

        ApiResult result = await client.PostAsync($"/CloudLogin/Token/FromRequest?requestId={code}&audience=app-a", new { }, AuthorityClient.Basic("app-a", "secret-a"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
    }

    [Fact]
    public async Task TheConsumerCookieCarriesNoCredential_AndTheTokensStayServerSide()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = ecosystem.NewBrowser();

        await EcosystemFlow.LoginAsync(browser, app);

        foreach ((string host, string name, string value) in browser.Cookies)
        {
            Assert.DoesNotContain("secret-for-", value);
            Assert.DoesNotContain("eyJ", value);
            Assert.DoesNotContain("refresh", value, StringComparison.OrdinalIgnoreCase);
            _ = (host, name);
        }
    }
}
