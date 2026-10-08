using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>
/// Websites without a backend and native apps: nothing registered and no secret. The origin of the return address is the identity, PKCE is
/// mandatory, and the tokens are valid only for that origin.
/// </summary>
public class PublicClientTests
{
    private sealed record PublicApp(AuthEcosystem Ecosystem, string ClientId, string Callback, string InitiatorHost);

    private const string SiteOrigin = "https://spa-app.test";
    private const string SiteCallback = SiteOrigin + "/auth/callback";

    private static async Task<PublicApp> StartSiteAsync()
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();

        // Serves the site's pages so the browser has somewhere to land after a logout; the site itself never presents a key.
        await ecosystem.AddConsumerAsync("spa-app");

        return new PublicApp(ecosystem, SiteOrigin, SiteCallback, "spa-app.test");
    }

    private static async Task<PublicApp> StartNativeAsync() =>
        new(await AuthEcosystem.CreateAsync(), "myapp:", "myapp://auth/callback", "myapp");

    private static string AuthorizeUrl(string redirect, string state, string? challenge, string? method = "S256") =>
        $"{AuthorityHost.Origin}/CloudLogin/Authorize?redirect_uri={Uri.EscapeDataString(redirect)}&state={Uri.EscapeDataString(state)}"
        + (challenge is null ? string.Empty : $"&code_challenge={Uri.EscapeDataString(challenge)}")
        + (method is null ? string.Empty : $"&code_challenge_method={method}");

    private static async Task<(string Code, string State)> SignInAsync(PublicApp app, TestBrowser browser, string verifier, string? state = null, string? redirect = null)
    {
        state ??= CloudLoginPkce.CreateState();
        BrowserResponse started = await browser.NavigateAsync(AuthorizeUrl(redirect ?? app.Callback, state, CloudLoginPkce.CreateChallenge(verifier)), initiatorHost: app.InitiatorHost);
        string? reference = started.Query("referer");

        Assert.True(reference is not null, $"The authority did not start a transaction: {started.Status} {started.FinalUrl} {started.Body}");

        if (browser.CookieValue(new Uri(AuthorityHost.Origin).Host, "__Host-CloudLogin") is null)
            await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);

        return EcosystemFlow.CodeAndState(await EcosystemFlow.CallbackUrlAsync(browser, reference!));
    }

    private static async Task<ApiResult> RedeemAsync(PublicApp app, string code, string verifier, string? redirect = null, string? clientId = null, string? audience = null) =>
        await new AuthorityClient(app.Ecosystem.Network).RedeemAsync(null, code, verifier, redirect ?? app.Callback, clientId: clientId ?? app.ClientId, audience: audience);

    [Fact]
    public async Task AWebsiteWithoutABackend_SignsInWithCodeAndPkce_WithoutRegistrationOrSecret_AndIsGivenNoRefreshToken()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        string state = CloudLoginPkce.CreateState();

        (string code, string returnedState) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier, state);
        ApiResult tokens = await RedeemAsync(app, code, verifier);

        Assert.Equal(state, returnedState);
        Assert.Equal(HttpStatusCode.OK, tokens.Status);
        Assert.Equal(SiteOrigin, new JsonWebToken(tokens.Property("access_token")!).Audiences.Single());
        Assert.True(string.IsNullOrEmpty(tokens.Property("refresh_token")));
        Assert.Equal(ApplicationKinds.Website, app.Ecosystem.Authority.Applications.Documents[SiteOrigin].Kind);
    }

    [Theory]
    [InlineData("portal")]
    [InlineData("https://other.test")]
    public async Task AWebsiteWithoutABackend_CannotAskForATokenForAnotherAudience(string audience)
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);

        ApiResult refused = await RedeemAsync(app, code, verifier, audience: audience);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("invalid_target", refused.Property("error"));
    }

    [Fact]
    public async Task ACodeIssuedToOneOrigin_CannotBeRedeemedByClaimingAnother()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);

        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, verifier, clientId: "https://other.test")).Status);
    }

    [Fact]
    public async Task APublicCode_CannotBeRedeemedWithoutTheVerifier_OrWithAWrongOne_AndIsBurnedByTheAttempt()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);

        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, "")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, CloudLoginPkce.CreateVerifier())).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, verifier)).Status);
    }

    [Fact]
    public async Task APublicCode_IsSingleUse()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);

        Assert.Equal(HttpStatusCode.OK, (await RedeemAsync(app, code, verifier)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, verifier)).Status);
    }

    [Fact]
    public async Task ACodeIsBoundToTheRedirectItWasIssuedFor()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);

        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, verifier, redirect: $"{SiteOrigin}/other")).Status);
    }

    [Theory]
    [InlineData("http://spa-app.test/auth/callback")]
    [InlineData("//evil.test/auth/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@spa-app.test/auth/callback")]
    public async Task AnUnusableReturnAddress_IsRefused(string redirect)
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;

        BrowserResponse response = await app.Ecosystem.NewBrowser().NavigateAsync(AuthorizeUrl(redirect, CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier())), initiatorHost: app.InitiatorHost);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Null(response.Query("referer"));
    }

    [Fact]
    public async Task AnotherSitesSignIn_ReturnsToThatSite_AndYieldsTokensOnlyForIt()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        TestBrowser browser = app.Ecosystem.NewBrowser();

        BrowserResponse started = await browser.NavigateAsync(AuthorizeUrl("https://evil.test/collect", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier)), initiatorHost: "evil.test");
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        string callback = await EcosystemFlow.CallbackUrlAsync(browser, started.Query("referer")!);
        (string code, _) = EcosystemFlow.CodeAndState(callback);

        Assert.Equal("evil.test", new Uri(callback).Host);
        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, verifier, redirect: "https://evil.test/collect")).Status);

        ApiResult evilTokens = await RedeemAsync(app, code, verifier, redirect: "https://evil.test/collect", clientId: "https://evil.test");
        Assert.Equal("https://evil.test", new JsonWebToken(evilTokens.Property("access_token")!).Audiences.Single());
    }

    [Theory]
    [InlineData(null, "S256")]
    [InlineData("short", "S256")]
    [InlineData("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "plain")]
    public async Task PkceIsMandatoryForAPublicClient_AndOnlyS256Counts(string? challenge, string? method)
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;

        BrowserResponse response = await app.Ecosystem.NewBrowser().NavigateAsync(AuthorizeUrl(app.Callback, CloudLoginPkce.CreateState(), challenge, method), initiatorHost: app.InitiatorHost);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task ABlockedWebsite_CannotStartASignIn_OrRedeemACodeIssuedBefore()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        TestBrowser browser = app.Ecosystem.NewBrowser();
        (string code, _) = await SignInAsync(app, browser, verifier);

        await app.Ecosystem.Authority.Admin.BlockAsync(Guid.NewGuid(), SiteOrigin, "abuse");

        Assert.Equal(HttpStatusCode.Unauthorized, (await RedeemAsync(app, code, verifier)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.NavigateAsync(AuthorizeUrl(app.Callback, CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier)), initiatorHost: app.InitiatorHost)).Status);
    }

    [Fact]
    public async Task ABackend_CannotBeImpersonatedByItsNameAlone()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost backend = await ecosystem.AddConsumerAsync("web-app");
        TokenSet issued = await TokenSets.IssueAsync(ecosystem, backend, backend.Secret);

        ApiResult redeemed = await new AuthorityClient(ecosystem.Network).RedeemAsync(null, issued.Code, issued.Verifier, issued.Redirect, clientId: "web-app");
        ApiResult refreshed = await new AuthorityClient(ecosystem.Network).RefreshAsync(null, issued.Refresh!, clientId: "web-app");

        Assert.Equal(HttpStatusCode.Unauthorized, redeemed.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.Status);
    }

    [Fact]
    public async Task APublicClient_CannotUseTheEndpointsThatNeedASecretKey()
    {
        PublicApp app = await StartSiteAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        AuthorityClient client = new(app.Ecosystem.Network);

        ApiResult begin = await client.PostAsync("/CloudLogin/Authorize/Begin", new { returnUrl = app.Callback, state = CloudLoginPkce.CreateState(), codeChallenge = CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()), codeChallengeMethod = "S256" });
        ApiResult status = await client.PostAsync("/CloudLogin/Token/SessionStatus", new { sessionId = "anything" });

        Assert.Equal(HttpStatusCode.Unauthorized, begin.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, status.Status);
    }

    [Fact]
    public async Task ANativeApp_GetsARefreshToken_AndRefreshesWithItsSchemeAlone_WithRotationAndReuseDetection()
    {
        PublicApp app = await StartNativeAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);
        ApiResult tokens = await RedeemAsync(app, code, verifier);
        AuthorityClient client = new(app.Ecosystem.Network);

        Assert.Equal(HttpStatusCode.OK, tokens.Status);
        Assert.Equal("myapp:", new JsonWebToken(tokens.Property("access_token")!).Audiences.Single());
        Assert.Equal(ApplicationKinds.NativeApp, app.Ecosystem.Authority.Applications.Documents["myapp:"].Kind);
        string refresh = tokens.Property("refresh_token")!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(null, refresh)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(null, refresh, clientId: "otherapp:")).Status);

        ApiResult rotated = await client.RefreshAsync(null, refresh, clientId: app.ClientId);
        Assert.Equal(HttpStatusCode.OK, rotated.Status);
        Assert.NotEqual(refresh, rotated.Property("refresh_token"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(null, refresh, clientId: app.ClientId)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(null, rotated.Property("refresh_token")!, clientId: app.ClientId)).Status);
    }

    [Fact]
    public async Task ANativeAppsCode_IsBoundToItsScheme()
    {
        PublicApp app = await StartNativeAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, string state) = await SignInAsync(app, app.Ecosystem.NewBrowser(), verifier);

        Assert.False(string.IsNullOrEmpty(state));
        Assert.Equal(HttpStatusCode.BadRequest, (await RedeemAsync(app, code, verifier, redirect: "otherapp://auth/callback", clientId: "otherapp:")).Status);
        Assert.Equal(HttpStatusCode.OK, (await RedeemAsync(app, code, verifier)).Status);
    }

    private static async Task<(PublicApp App, TestBrowser Browser, string Access)> SignedInAsync()
    {
        PublicApp app = await StartSiteAsync();
        TestBrowser browser = app.Ecosystem.NewBrowser();
        string verifier = CloudLoginPkce.CreateVerifier();
        (string code, _) = await SignInAsync(app, browser, verifier);
        ApiResult tokens = await RedeemAsync(app, code, verifier);
        return (app, browser, tokens.Property("access_token")!);
    }

    private static Task<ApiResult> BeginLogoutAsync(PublicApp app, string? bearer, string destination, string? clientId = null) =>
        new AuthorityClient(app.Ecosystem.Network).PostAsync("/CloudLogin/Authorize/Logout", new { postLogoutRedirectUri = destination, clientId = clientId ?? app.ClientId }, bearer is null ? null : $"Bearer {bearer}");

    [Fact]
    public async Task APublicClient_EndsTheAuthoritySession_ByProvingItHoldsALiveTokenForIt()
    {
        (PublicApp app, TestBrowser browser, string access) = await SignedInAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;

        ApiResult begun = await BeginLogoutAsync(app, access, $"{SiteOrigin}/signed-out");
        Assert.Equal(HttpStatusCode.OK, begun.Status);

        BrowserResponse result = await browser.NavigateAsync(begun.Property("logoutUrl")!, initiatorHost: app.InitiatorHost);

        Assert.Equal("/signed-out", result.FinalUrl.AbsolutePath);
        Assert.Equal(app.InitiatorHost, result.FinalUrl.Host);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: "login.test")).Status);
    }

    [Fact]
    public async Task APublicClientCannotOpenALogoutTransaction_WithoutALiveTokenOfItsOwn()
    {
        (PublicApp app, _, string access) = await SignedInAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        ConsumerHost backend = await app.Ecosystem.AddConsumerAsync("web-app");
        TokenSet othersToken = await TokenSets.IssueAsync(app.Ecosystem, backend, backend.Secret);

        Assert.Equal(HttpStatusCode.Unauthorized, (await BeginLogoutAsync(app, null, $"{SiteOrigin}/signed-out")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BeginLogoutAsync(app, "not.a.token", $"{SiteOrigin}/signed-out")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BeginLogoutAsync(app, access + "x", $"{SiteOrigin}/signed-out")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BeginLogoutAsync(app, othersToken.Access, $"{SiteOrigin}/signed-out")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BeginLogoutAsync(app, access, "https://web-app.test/signed-out", clientId: "https://web-app.test")).Status);
    }

    [Fact]
    public async Task APublicClientsLogoutToken_StopsWorkingOnceItsSessionHasEnded()
    {
        (PublicApp app, _, string access) = await SignedInAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;
        string sessionId = new JsonWebToken(access).GetClaim("sid").Value;

        await app.Ecosystem.Authority.SessionService.RevokeSessionAsync(sessionId, SessionRevocationReasons.UserSignedOut);

        Assert.Equal(HttpStatusCode.Unauthorized, (await BeginLogoutAsync(app, access, $"{SiteOrigin}/signed-out")).Status);
    }

    [Theory]
    [InlineData("https://evil.test/after")]
    [InlineData("https://spa-app.test.evil.test/after")]
    [InlineData("http://spa-app.test/after")]
    [InlineData("javascript:alert(1)")]
    public async Task APublicClientsLogoutDestination_MustBeOnItsOwnOrigin(string destination)
    {
        (PublicApp app, _, string access) = await SignedInAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;

        Assert.Equal(HttpStatusCode.BadRequest, (await BeginLogoutAsync(app, access, destination)).Status);
    }

    [Fact]
    public async Task AnAttackersPageCannotLogAVictimOutOfAPublicClient_WithoutTheirToken()
    {
        (PublicApp app, TestBrowser browser, _) = await SignedInAsync();
        await using AuthEcosystem owner_ = app.Ecosystem;

        await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Logout", initiatorHost: "attacker.example");

        Assert.Equal(HttpStatusCode.OK, (await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: "login.test")).Status);
    }
}
