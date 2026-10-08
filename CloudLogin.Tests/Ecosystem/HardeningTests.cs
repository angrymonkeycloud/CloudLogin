using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>Failures must not lift restrictions, and no refresh path may exist without the client that owns it.</summary>
public class HardeningTests
{
    private const string AuthorityHostName = "login.test";

    private static string AuthorityCookie(TestBrowser browser) => string.Join("; ", browser.Cookies.Where(cookie => cookie.Host == AuthorityHostName).Select(cookie => $"{cookie.Name}={cookie.Value}"));

    private static AuthorityClient Client(AuthEcosystem ecosystem) => new(ecosystem.Network);

    private static void BreakRegistry(AuthEcosystem ecosystem, bool down)
    {
        ecosystem.Authority.Applications.Unavailable = down;
        ecosystem.Authority.SecretKeyStore.Unavailable = down;
        ecosystem.Authority.Directory.Invalidate();
    }

    // ── Policy reads ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ABlockedApplication_CannotStartRedeemOrRefresh_WhileTheRegistryIsUnreadable()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, "secret-a");
        string verifier = CloudLoginPkce.CreateVerifier();
        ApiResult pending = await Client(ecosystem).BeginAsync(AuthorityClient.Basic("app-a", "secret-a"), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier));

        await ecosystem.BlockAsync(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).BeginAsync(AuthorityClient.Basic("app-a", "secret-a"), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier))).Status);

        BreakRegistry(ecosystem, down: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).BeginAsync(AuthorityClient.Basic("app-a", "secret-a"), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", "secret-a"), tokens.Refresh!)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RedeemAsync(AuthorityClient.Basic("app-a", "secret-a"), tokens.Code, tokens.Verifier, tokens.Redirect)).Status);
        Assert.NotNull(pending.Property("transaction"));
    }

    [Fact]
    public async Task ARevokedDeploymentKey_CannotAuthenticate_WhileTheRegistryIsUnreadable()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "leaked");
        await ecosystem.Authority.Keys.RevokeAsync(Guid.NewGuid(), ClientDirectory.DeploymentKeyId(Server.Tokens.CloudLoginTokenOptions.HashSecret("leaked")), "leaked");

        BreakRegistry(ecosystem, down: true);

        ApiResult begun = await Client(ecosystem).BeginAsync(AuthorityClient.Basic("app-a", "leaked"), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.Unauthorized, begun.Status);
    }

    [Fact]
    public async Task ServiceReturnsWhenTheRegistryDoes()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");

        BreakRegistry(ecosystem, down: true);
        BreakRegistry(ecosystem, down: false);

        ApiResult begun = await Client(ecosystem).BeginAsync(AuthorityClient.Basic("app-a", "secret-a"), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.OK, begun.Status);
    }

    // ── The cookie-session token path ────────────────────────────────────────

    private static async Task<ApiResult> FromSessionAsync(AuthEcosystem ecosystem, TestBrowser browser, string audience) =>
        await Client(ecosystem).PostAsync($"/CloudLogin/Token/Session?audience={audience}", null, cookie: AuthorityCookie(browser));

    [Fact]
    public async Task TheCookieSessionPath_IssuesNoRefreshToken_ByDefault()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);

        ApiResult issued = await FromSessionAsync(ecosystem, browser, "app-a");

        Assert.Equal(HttpStatusCode.OK, issued.Status);
        Assert.False(string.IsNullOrEmpty(issued.Property("access_token")));
        Assert.True(string.IsNullOrEmpty(issued.Property("refresh_token")));
        Assert.DoesNotContain(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionTokenDocument>(), token => token.FamilyId is not null && ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Any(family => family.FamilyId == token.FamilyId && family.Audience == "app-a"));
    }

    [Fact]
    public async Task NoRefreshTokenExists_ThatCanBeRefreshedWithoutItsClientAuthenticating()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        await ecosystem.AddConsumerAsync("app-b", secret: "secret-b");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        TokenSet bound = await TokenSets.IssueAsync(ecosystem, app, "secret-a");

        ApiResult fromSession = await FromSessionAsync(ecosystem, browser, "app-a");
        string? sessionRefresh = fromSession.Property("refresh_token");

        Assert.True(string.IsNullOrEmpty(sessionRefresh));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(null, bound.Refresh!)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-b", "secret-b"), bound.Refresh!)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", "secret-a"), bound.Refresh!)).Status);
    }

    [Fact]
    public async Task TheLegacyUnboundRefreshToken_ExistsOnlyWhenExplicitlyEnabled_AndIsRefusedOnceItIsTurnedOff()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync(options => options.AllowUnboundRefreshTokens = true);
        await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);

        ApiResult legacy = await FromSessionAsync(ecosystem, browser, "app-a");
        string refresh = legacy.Property("refresh_token")!;

        Assert.False(string.IsNullOrEmpty(refresh));
        Assert.Equal(HttpStatusCode.OK, (await Client(ecosystem).RefreshAsync(null, refresh)).Status);

        ecosystem.Authority.Services.GetRequiredService<IOptions<Server.Tokens.CloudLoginTokenOptions>>().Value.AllowUnboundRefreshTokens = false;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(null, refresh)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", "secret-a"), refresh)).Status);
    }

    // ── Back-channel processing ──────────────────────────────────────────────

    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost App, TestBrowser Browser, string SessionId)> SignedInAsync(int instances = 1)
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", instances, secret: "secret-a");
        TestBrowser browser = ecosystem.NewBrowser();
        app.Balancer.Instance = 0;
        await EcosystemFlow.LoginAsync(browser, app);
        app.Balancer.Instance = -1;
        string sessionId = (await EcosystemFlow.MeAsync(browser, app)).SessionId!;
        return (ecosystem, app, browser, sessionId);
    }

    private static async Task<bool> SignedIn(TestBrowser browser, ConsumerHost app) => (await EcosystemFlow.MeAsync(browser, app)).Authenticated;

    [Fact]
    public async Task AFailedRevocationWrite_IsNotAcknowledged_AndTheRetryApplies()
    {
        (AuthEcosystem ecosystem, ConsumerHost app, TestBrowser browser, string sessionId) = await SignedInAsync();
        await using AuthEcosystem owner_ = ecosystem;
        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: sessionId));

        app.Cache.FailNext(1, "cloudlogin:sid:");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await LogoutTokens.PostAsync(ecosystem, app, token));
        Assert.True(await SignedIn(browser, app));

        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, app, token));
        Assert.False(await SignedIn(browser, app));
        Assert.Equal(1, app.Cache.Failed);
    }

    [Fact]
    public async Task AFailureMarkingTheTokenProcessed_IsRetried_AndStillConverges()
    {
        (AuthEcosystem ecosystem, ConsumerHost app, TestBrowser browser, string sessionId) = await SignedInAsync();
        await using AuthEcosystem owner_ = ecosystem;
        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: sessionId));

        app.Cache.FailNext(1, "cloudlogin:jti:");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await LogoutTokens.PostAsync(ecosystem, app, token));
        Assert.False(await SignedIn(browser, app));

        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, app, token));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, app, token));
    }

    [Fact]
    public async Task TheAuthoritysOwnRetries_ApplyANotification_AfterATransientRevocationFailure()
    {
        (AuthEcosystem ecosystem, ConsumerHost app, TestBrowser browser, string sessionId) = await SignedInAsync();
        await using AuthEcosystem owner_ = ecosystem;
        SessionFamilyDocument family = ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Single(item => item.SessionId == sessionId && item.Audience == SessionService.BrowserAudience);

        app.Cache.FailNext(1, "cloudlogin:sid:");
        await ecosystem.Authority.SessionService.RevokeFamilyAsync(family.FamilyId, SessionRevocationReasons.AdminRevoked);

        Assert.False(await SignedIn(browser, app));
        Assert.Equal(0, ecosystem.Authority.BackChannel.Failed);
        Assert.Equal(1, ecosystem.Authority.BackChannel.Delivered);
        Assert.Equal(1, app.Cache.Failed);
    }

    [Fact]
    public async Task TheSameNotification_ArrivingAtEveryInstanceAtOnce_IsAppliedOnce_AndNeverHarmsALaterSession()
    {
        (AuthEcosystem ecosystem, ConsumerHost app, TestBrowser browser, _) = await SignedInAsync(instances: 3);
        await using AuthEcosystem owner_ = ecosystem;
        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", Subject: ecosystem.User.Id.ToString(), RevokedBeforeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        HttpStatusCode[] first = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => LogoutTokens.PostAsync(ecosystem, app, token)));
        Assert.All(first, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.False(await SignedIn(browser, app));

        await Task.Delay(20);
        await EcosystemFlow.LoginAsync(browser, app, authorityAlreadySignedIn: true);
        Assert.True(await SignedIn(browser, app));

        HttpStatusCode[] replay = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => LogoutTokens.PostAsync(ecosystem, app, token)));
        Assert.All(replay, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.True(await SignedIn(browser, app));
    }

    [Fact]
    public async Task ASubjectNotification_EndsOnlyWhatTheAuthorityIssuedBeforeItsOwnTimestamp()
    {
        (AuthEcosystem ecosystem, ConsumerHost app, TestBrowser browser, _) = await SignedInAsync();
        await using AuthEcosystem owner_ = ecosystem;
        string subject = ecosystem.User.Id.ToString();

        string older = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", Subject: subject, RevokedBeforeMs: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds()));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, app, older));
        Assert.True(await SignedIn(browser, app));

        string newer = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", Subject: subject, RevokedBeforeMs: DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, app, newer));
        Assert.False(await SignedIn(browser, app));
    }

    [Fact]
    public async Task ARevocationTimestampFarInTheFuture_IsRefused_SoItCannotEndSessionsNotYetIssued()
    {
        (AuthEcosystem ecosystem, ConsumerHost app, TestBrowser browser, _) = await SignedInAsync();
        await using AuthEcosystem owner_ = ecosystem;
        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", Subject: ecosystem.User.Id.ToString(), RevokedBeforeMs: DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeMilliseconds()));

        Assert.Equal(HttpStatusCode.BadRequest, await LogoutTokens.PostAsync(ecosystem, app, token));
        Assert.True(await SignedIn(browser, app));
    }
}
