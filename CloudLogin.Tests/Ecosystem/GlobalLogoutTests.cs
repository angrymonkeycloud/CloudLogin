using System.Net;
using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

internal sealed record LogoutTokenSpec(
    string Audience,
    string? Subject = null,
    string? SessionId = null,
    string? TokenId = "auto",
    string? Issuer = null,
    string TokenType = "logout+jwt",
    bool IncludeEvent = true,
    bool IncludeNonce = false,
    TimeSpan? Age = null,
    TimeSpan? Lifetime = null,
    SigningCredentials? Key = null,
    long? RevokedBeforeMs = null);

internal static class LogoutTokens
{
    public static async Task<string> CreateAsync(AuthEcosystem ecosystem, LogoutTokenSpec spec)
    {
        SigningCredentials credentials = spec.Key ?? (await ecosystem.Authority.Services.GetRequiredService<CloudLoginSigningKeyManager>().GetSigningCredentialsAsync()).Credentials;
        DateTime issuedAt = DateTime.UtcNow - (spec.Age ?? TimeSpan.Zero);

        Dictionary<string, object> claims = new(StringComparer.Ordinal);

        if (spec.Subject is not null)
            claims["sub"] = spec.Subject;

        if (spec.SessionId is not null)
            claims["sid"] = spec.SessionId;

        if (spec.TokenId is not null)
            claims["jti"] = spec.TokenId == "auto" ? Guid.NewGuid().ToString("N") : spec.TokenId;

        if (spec.IncludeEvent)
            claims["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() };

        if (spec.RevokedBeforeMs is { } revokedBefore)
            claims["revoked_before_ms"] = revokedBefore;

        if (spec.IncludeNonce)
            claims["nonce"] = "n-0S6_WzA2Mj";

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer ?? AuthorityHost.Origin,
            Audience = spec.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + (spec.Lifetime ?? TimeSpan.FromMinutes(2)),
            SigningCredentials = credentials,
            Claims = claims,
            TokenType = spec.TokenType
        });
    }

    public static SigningCredentials Rogue(string? keyId)
    {
        ECDsaSecurityKey key = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = keyId };
        return new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
    }

    public static async Task<HttpStatusCode> PostAsync(AuthEcosystem ecosystem, ConsumerHost app, string? token)
    {
        using HttpClient http = new(ecosystem.Network, disposeHandler: false);
        using FormUrlEncodedContent content = new(new Dictionary<string, string> { ["logout_token"] = token ?? string.Empty });
        using HttpResponseMessage response = await http.PostAsync($"{app.Origin}/auth/backchannel-logout", content);
        return response.StatusCode;
    }

    public static string Unsigned(string audience)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string payload = "{\"iss\":\"" + AuthorityHost.Origin + "\",\"aud\":\"" + audience + "\",\"iat\":" + now + ",\"exp\":" + (now + 120) + ",\"jti\":\"" + Guid.NewGuid().ToString("N") + "\",\"sid\":\"any\",\"events\":{\"http://schemas.openid.net/event/backchannel-logout\":{}}}";
        return $"{Encode("""{"alg":"none","typ":"logout+jwt"}""")}.{Encode(payload)}.";
    }
}

internal sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
{
    public int Requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        return Task.FromResult(new HttpResponseMessage(status));
    }
}

public class GlobalLogoutTests
{
    private sealed record Device(TestBrowser Browser, ConsumerHost A, ConsumerHost B, string SessionId);

    private static async Task<AuthEcosystem> StartAsync(Action<Dictionary<string, string?>>? configureConsumer = null, int instances = 1)
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        await ecosystem.AddConsumerAsync("app-a", instances, secret: "secret-a", configureConsumer: configureConsumer);
        await ecosystem.AddConsumerAsync("app-b", secret: "secret-b", configureConsumer: configureConsumer);
        return ecosystem;
    }

    private static async Task<Device> SignInBothAsync(AuthEcosystem ecosystem, string email = AuthEcosystem.AuthorityEmail, TestBrowser? browser = null)
    {
        browser ??= ecosystem.NewBrowser();
        ConsumerHost a = ecosystem.Consumers[0];
        ConsumerHost b = ecosystem.Consumers[1];

        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser, email);
        await EcosystemFlow.LoginAsync(browser, a, authorityAlreadySignedIn: true);
        await EcosystemFlow.LoginAsync(browser, b, authorityAlreadySignedIn: true);

        (bool authenticatedA, string? sessionA) = await EcosystemFlow.MeAsync(browser, a);
        (bool authenticatedB, string? sessionB) = await EcosystemFlow.MeAsync(browser, b);

        Assert.True(authenticatedA && authenticatedB);
        Assert.Equal(sessionA, sessionB);
        return new Device(browser, a, b, sessionA!);
    }

    private static async Task<bool> SignedInAsync(TestBrowser browser, ConsumerHost app) => (await EcosystemFlow.MeAsync(browser, app)).Authenticated;

    private static async Task<bool> AuthoritySignedInAsync(TestBrowser browser) =>
        (await browser.NavigateAsync($"{AuthorityHost.Origin}/CloudLogin/Login/Complete?referer=/Account", initiatorHost: "login.test")).Status == HttpStatusCode.OK;

    private static AdminSessionService AdminSessions(AuthEcosystem ecosystem) => new(
        ecosystem.Authority.Sessions,
        ecosystem.Authority.SessionService,
        new Tests.Core.InMemoryUserRepository(),
        ecosystem.Authority.Admin,
        ecosystem.Authority.Services.GetRequiredService<IAuditLogger>());

    private static SessionFamilyDocument BrowserFamily(AuthEcosystem ecosystem, string sessionId) =>
        ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Single(family => family.SessionId == sessionId && family.Audience == SessionService.BrowserAudience);

    [Fact]
    public async Task SigningOutOfOneApplication_EndsTheSessionInEveryOtherApplication_EachWithItsOwnCookie()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);

        Assert.Contains(device.Browser.Cookies, cookie => cookie.Host == device.A.Host && cookie.Name == "app-a.session");
        Assert.Contains(device.Browser.Cookies, cookie => cookie.Host == device.B.Host && cookie.Name == "app-b.session");
        Assert.DoesNotContain(device.Browser.Cookies, cookie => cookie.Host == device.A.Host && cookie.Name == "app-b.session");

        await device.Browser.NavigateAsync($"{device.A.Origin}/auth/logout?returnUrl=/", initiatorHost: device.A.Host);

        Assert.False(await SignedInAsync(device.Browser, device.A));
        Assert.False(await SignedInAsync(device.Browser, device.B));
        Assert.False(await AuthoritySignedInAsync(device.Browser));
        Assert.True(ecosystem.Authority.BackChannel.Delivered >= 2);
        Assert.Equal(0, ecosystem.Authority.BackChannel.Failed);
        Assert.Contains(ecosystem.Authority.Audit.Events, item => item.EventType == AuditEventTypes.LogoutNotified && item.ClientId == "app-b");
    }

    [Fact]
    public async Task SigningOutOnePerson_NeverEndsAnotherPersonsSessions()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        ecosystem.AddUser("other@example.com");
        Device mine = await SignInBothAsync(ecosystem);
        Device theirs = await SignInBothAsync(ecosystem, "other@example.com");

        await mine.Browser.NavigateAsync($"{mine.A.Origin}/auth/logout?returnUrl=/", initiatorHost: mine.A.Host);

        Assert.False(await SignedInAsync(mine.Browser, mine.B));
        Assert.True(await SignedInAsync(theirs.Browser, theirs.A));
        Assert.True(await SignedInAsync(theirs.Browser, theirs.B));
        Assert.NotEqual(mine.SessionId, theirs.SessionId);
    }

    [Fact]
    public async Task AnAdministratorRevokingTheDeviceSession_EndsItInEveryApplication()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);

        await AdminSessions(ecosystem).RevokeAsync(Guid.NewGuid(), BrowserFamily(ecosystem, device.SessionId).FamilyId);

        Assert.False(await SignedInAsync(device.Browser, device.A));
        Assert.False(await SignedInAsync(device.Browser, device.B));
        Assert.False(await AuthoritySignedInAsync(device.Browser));
    }


    [Fact]
    public async Task RevokingTheDevicesAuthoritySession_AlsoKillsTheApplicationRefreshTokensIssuedFromIt()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, "secret-a");
        AuthorityClient client = new(ecosystem.Network);

        await AdminSessions(ecosystem).RevokeAsync(Guid.NewGuid(), BrowserFamily(ecosystem, tokens.SessionId).FamilyId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(AuthorityClient.Basic("app-a", "secret-a"), tokens.Refresh!)).Status);
        Assert.Equal("False", (await client.PostAsync("/CloudLogin/Token/SessionStatus", new { sessionId = tokens.SessionId }, AuthorityClient.Basic("app-a", "secret-a"))).Property("active"));
        Assert.All(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Where(family => family.SessionId == tokens.SessionId), family => Assert.True(family.IsRevoked));
    }
    [Fact]
    public async Task RevokingOneApplicationsSessions_LeavesTheOtherApplicationsSignedIn()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);

        int revoked = await AdminSessions(ecosystem).RevokeForApplicationAsync(Guid.NewGuid(), "app-a");

        Assert.True(revoked >= 1);
        Assert.False(await SignedInAsync(device.Browser, device.A));
        Assert.True(await SignedInAsync(device.Browser, device.B));
    }

    [Fact]
    public async Task SignOutEverywhere_EndsEveryDeviceInEveryApplication_AndALaterSignInStillWorks()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device first = await SignInBothAsync(ecosystem);
        Device second = await SignInBothAsync(ecosystem);

        await ecosystem.Authority.SessionService.RevokeAllForUserAsync(ecosystem.User.Id, SessionRevocationReasons.AdminRevoked);

        foreach (Device device in new[] { first, second })
        {
            Assert.False(await SignedInAsync(device.Browser, device.A));
            Assert.False(await SignedInAsync(device.Browser, device.B));
        }

        Device again = await SignInBothAsync(ecosystem, browser: first.Browser);
        Assert.NotEqual(first.SessionId, again.SessionId);

        await ecosystem.Authority.SessionService.RevokeAllForUserAsync(ecosystem.User.Id, SessionRevocationReasons.AdminRevoked);

        Assert.False(await SignedInAsync(again.Browser, again.A));
        Assert.False(await SignedInAsync(again.Browser, again.B));
    }

    [Fact]
    public async Task DisablingAUser_EndsTheirSessionsEverywhere()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        Tests.Core.InMemoryUserRepository users = new();
        await users.CreateAsync(new UserDocument { Id = ecosystem.User.Id.ToString(), State = UserStates.Active });

        AdminUserService service = new(users, new Tests.Core.InMemoryCredentialRepository(), ecosystem.Authority.SessionService, ecosystem.Authority.Services.GetRequiredService<IAuditLogger>());
        await service.SetDisabledAsync(Guid.NewGuid(), ecosystem.User.Id, disabled: true);

        Assert.False(await SignedInAsync(device.Browser, device.A));
        Assert.False(await SignedInAsync(device.Browser, device.B));
    }

    [Fact]
    public async Task AUserLevelNotification_IsNeverSuppressedAsADuplicateOfAnEarlierOne()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        await SignInBothAsync(ecosystem);
        long before = ecosystem.Authority.BackChannel.Delivered;

        await ecosystem.Authority.BackChannel.NotifyUserAsync(ecosystem.User.Id);
        long afterFirst = ecosystem.Authority.BackChannel.Delivered;
        await ecosystem.Authority.BackChannel.NotifyUserAsync(ecosystem.User.Id);

        Assert.Equal(before + 2, afterFirst);
        Assert.Equal(afterFirst + 2, ecosystem.Authority.BackChannel.Delivered);
    }

    [Fact]
    public async Task ARevocationReachesEveryInstanceOfAnApplication_ThroughTheSharedCache()
    {
        await using AuthEcosystem ecosystem = await StartAsync(instances: 2);
        ConsumerHost a = ecosystem.Consumers[0];
        TestBrowser browser = ecosystem.NewBrowser();

        a.Balancer.Instance = 0;
        await EcosystemFlow.LoginAsync(browser, a);
        Assert.True(await SignedInAsync(browser, a));

        a.Balancer.Instance = 1;
        Assert.True(await SignedInAsync(browser, a));

        a.Balancer.Instance = 1;
        string sessionId = (await EcosystemFlow.MeAsync(browser, a)).SessionId!;
        await AdminSessions(ecosystem).RevokeAsync(Guid.NewGuid(), BrowserFamily(ecosystem, sessionId).FamilyId);

        a.Balancer.Instance = 0;
        Assert.False(await SignedInAsync(browser, a));
        a.Balancer.Instance = 1;
        Assert.False(await SignedInAsync(browser, a));
    }

    [Fact]
    public async Task ANotificationDeliveredToAnyOneInstance_RevokesTheSessionOnAllOfThem()
    {
        await using AuthEcosystem ecosystem = await StartAsync(instances: 3);
        ConsumerHost a = ecosystem.Consumers[0];
        TestBrowser browser = ecosystem.NewBrowser();
        a.Balancer.Instance = 0;
        await EcosystemFlow.LoginAsync(browser, a);
        string sessionId = (await EcosystemFlow.MeAsync(browser, a)).SessionId!;

        a.Balancer.Instance = 2;
        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: sessionId));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, a, token));

        foreach (int instance in new[] { 0, 1, 2 })
        {
            a.Balancer.Instance = instance;
            Assert.False(await SignedInAsync(browser, a));
        }
    }

    public static TheoryData<string> RejectedNotifications() =>
    [
        "rogue-signature",
        "other-audience",
        "wrong-issuer",
        "plain-jwt-type",
        "expired",
        "overlong-lifetime",
        "missing-event",
        "nonce-present",
        "no-session-or-subject",
        "no-token-id",
        "unsigned",
        "garbage",
        "empty"
    ];

    [Theory]
    [MemberData(nameof(RejectedNotifications))]
    public async Task AForgedOrMalformedNotification_IsRefused_AndTheSessionSurvives(string kind)
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        string keyId = (await ecosystem.Authority.Services.GetRequiredService<CloudLoginSigningKeyManager>().GetSigningCredentialsAsync()).Credentials.Key.KeyId;
        LogoutTokenSpec valid = new("app-a", SessionId: device.SessionId);

        string? token = kind switch
        {
            "rogue-signature" => await LogoutTokens.CreateAsync(ecosystem, valid with { Key = LogoutTokens.Rogue(keyId) }),
            "other-audience" => await LogoutTokens.CreateAsync(ecosystem, valid with { Audience = "app-b" }),
            "wrong-issuer" => await LogoutTokens.CreateAsync(ecosystem, valid with { Issuer = "https://evil.test" }),
            "plain-jwt-type" => await LogoutTokens.CreateAsync(ecosystem, valid with { TokenType = "JWT" }),
            "expired" => await LogoutTokens.CreateAsync(ecosystem, valid with { Age = TimeSpan.FromMinutes(10) }),
            "overlong-lifetime" => await LogoutTokens.CreateAsync(ecosystem, valid with { Lifetime = TimeSpan.FromHours(2) }),
            "missing-event" => await LogoutTokens.CreateAsync(ecosystem, valid with { IncludeEvent = false }),
            "nonce-present" => await LogoutTokens.CreateAsync(ecosystem, valid with { IncludeNonce = true }),
            "no-session-or-subject" => await LogoutTokens.CreateAsync(ecosystem, valid with { SessionId = null }),
            "no-token-id" => await LogoutTokens.CreateAsync(ecosystem, valid with { TokenId = null }),
            "unsigned" => LogoutTokens.Unsigned("app-a"),
            "garbage" => "not.a.jwt",
            _ => null
        };

        Assert.Equal(HttpStatusCode.BadRequest, await LogoutTokens.PostAsync(ecosystem, device.A, token));

        Assert.True(await SignedInAsync(device.Browser, device.A));
        Assert.True(await SignedInAsync(device.Browser, device.B));
    }

    [Fact]
    public async Task ANotificationSignedWithAnHmacKeyDerivedFromPublicData_IsRefused()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        string keyId = (await ecosystem.Authority.Services.GetRequiredService<CloudLoginSigningKeyManager>().GetSigningCredentialsAsync()).Credentials.Key.KeyId;
        SymmetricSecurityKey hmac = new(Encoding.UTF8.GetBytes("a-shared-secret-an-attacker-could-guess-0123456789")) { KeyId = keyId };

        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: device.SessionId, Key: new SigningCredentials(hmac, SecurityAlgorithms.HmacSha256)));

        Assert.Equal(HttpStatusCode.BadRequest, await LogoutTokens.PostAsync(ecosystem, device.A, token));
        Assert.True(await SignedInAsync(device.Browser, device.A));
    }

    [Fact]
    public async Task ARefusedNotification_DoesNotConsumeItsTokenId_SoAGenuineOneStillWorks()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        string keyId = (await ecosystem.Authority.Services.GetRequiredService<CloudLoginSigningKeyManager>().GetSigningCredentialsAsync()).Credentials.Key.KeyId;
        const string tokenId = "contested-jti";

        string forged = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: device.SessionId, TokenId: tokenId, Key: LogoutTokens.Rogue(keyId)));
        string genuine = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: device.SessionId, TokenId: tokenId));

        Assert.Equal(HttpStatusCode.BadRequest, await LogoutTokens.PostAsync(ecosystem, device.A, forged));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, device.A, genuine));
        Assert.False(await SignedInAsync(device.Browser, device.A));
    }

    [Fact]
    public async Task ADuplicateNotification_IsAcknowledged_AndCausesNoHarmToALaterSession()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", Subject: ecosystem.User.Id.ToString()));

        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, device.A, token));
        Assert.False(await SignedInAsync(device.Browser, device.A));

        await ecosystem.Authority.SessionService.RevokeAllForUserAsync(ecosystem.User.Id, SessionRevocationReasons.AdminRevoked);
        Device again = await SignInBothAsync(ecosystem, browser: device.Browser);
        Assert.True(await SignedInAsync(again.Browser, again.A));

        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, again.A, token));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, again.A, token));

        Assert.True(await SignedInAsync(again.Browser, again.A));
    }

    [Fact]
    public async Task ASubjectLevelNotification_EndsEarlierSessionsOnly()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        ecosystem.AddUser("other@example.com");
        Device other = await SignInBothAsync(ecosystem, "other@example.com");

        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", Subject: ecosystem.User.Id.ToString()));
        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, device.A, token));

        Assert.False(await SignedInAsync(device.Browser, device.A));
        Assert.True(await SignedInAsync(device.Browser, device.B));
        Assert.True(await SignedInAsync(other.Browser, other.A));
    }

    [Fact]
    public async Task ANotificationForASessionTheApplicationNeverSaw_IsHarmlessToOthers()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);

        string token = await LogoutTokens.CreateAsync(ecosystem, new LogoutTokenSpec("app-a", SessionId: "sess_unknown"));

        Assert.Equal(HttpStatusCode.OK, await LogoutTokens.PostAsync(ecosystem, device.A, token));
        Assert.True(await SignedInAsync(device.Browser, device.A));
    }

    [Fact]
    public async Task WhenDeliveryFails_ItIsRetriedABoundedNumberOfTimes_AndRecorded()
    {
        await using AuthEcosystem ecosystem = await StartAsync();
        Device device = await SignInBothAsync(ecosystem);
        HttpMessageHandler original = ecosystem.Network.Routes[device.A.Host];
        RecordingHandler down = new(HttpStatusCode.ServiceUnavailable);
        ecosystem.Network.Routes[device.A.Host] = down;

        await AdminSessions(ecosystem).RevokeAsync(Guid.NewGuid(), BrowserFamily(ecosystem, device.SessionId).FamilyId);
        ecosystem.Network.Routes[device.A.Host] = original;

        Assert.Equal(4, down.Requests);
        Assert.Equal(1, ecosystem.Authority.BackChannel.Failed);
        AuditEventDocument failure = Assert.Single(ecosystem.Authority.Audit.Events, item => item.EventType == AuditEventTypes.LogoutDeliveryFailed);
        Assert.Equal("app-a", failure.ClientId);
        Assert.Equal("4", failure.Data["Attempts"]);
        Assert.False(await SignedInAsync(device.Browser, device.B));
    }

    [Fact]
    public async Task ThePeriodicStatusCheck_BoundsHowLongAMissedNotificationCanKeepASessionAlive()
    {
        await using AuthEcosystem ecosystem = await StartAsync(settings => settings["CloudLogin:SessionRevalidationInterval"] = "00:00:01");
        Device device = await SignInBothAsync(ecosystem);
        HttpMessageHandler original = ecosystem.Network.Routes[device.A.Host];
        ecosystem.Network.Routes[device.A.Host] = new RecordingHandler(HttpStatusCode.ServiceUnavailable);

        await AdminSessions(ecosystem).RevokeAsync(Guid.NewGuid(), BrowserFamily(ecosystem, device.SessionId).FamilyId);
        ecosystem.Network.Routes[device.A.Host] = original;

        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.False(await SignedInAsync(device.Browser, device.A));
    }

    [Fact]
    public async Task AnAuthorityThatCannotBeReached_DoesNotSignAnyoneOut_WhileNothingIsRevoked()
    {
        await using AuthEcosystem ecosystem = await StartAsync(settings => settings["CloudLogin:SessionRevalidationInterval"] = "00:00:01");
        Device device = await SignInBothAsync(ecosystem);
        HttpMessageHandler authority = ecosystem.Network.Routes["login.test"];
        ecosystem.Network.Routes["login.test"] = new RecordingHandler(HttpStatusCode.BadGateway);

        await Task.Delay(TimeSpan.FromMilliseconds(1300));
        bool during = await SignedInAsync(device.Browser, device.A);
        ecosystem.Network.Routes["login.test"] = authority;

        Assert.True(during);
        Assert.True(await SignedInAsync(device.Browser, device.A));
    }

    [Fact]
    public async Task AfterSignOut_TheRefreshTokenIsDead_AndTheAccessTokenLivesNoLongerThanItsShortLifetime()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, "secret-a");
        AuthorityClient client = new(ecosystem.Network);

        await ecosystem.Authority.SessionService.RevokeSessionAsync(tokens.SessionId, SessionRevocationReasons.UserSignedOut);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(AuthorityClient.Basic("app-a", "secret-a"), tokens.Refresh!)).Status);
        ApiResult status = await client.PostAsync("/CloudLogin/Token/SessionStatus", new { sessionId = tokens.SessionId }, AuthorityClient.Basic("app-a", "secret-a"));
        Assert.Equal("False", status.Property("active"));

        JsonWebToken access = new(tokens.Access);
        Assert.True(access.ValidTo - access.ValidFrom <= ecosystem.Authority.TokenOptions.AccessTokenLifetime + TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TheSessionStatusEndpoint_IsOnlyAnsweredToAnAuthenticatedApplication()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, "secret-a");
        AuthorityClient client = new(ecosystem.Network);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/CloudLogin/Token/SessionStatus", new { sessionId = tokens.SessionId })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/CloudLogin/Token/SessionStatus", new { sessionId = tokens.SessionId }, AuthorityClient.Basic("app-a", "wrong"))).Status);
        Assert.Equal("True", (await client.PostAsync("/CloudLogin/Token/SessionStatus", new { sessionId = tokens.SessionId }, AuthorityClient.Basic("app-a", "secret-a"))).Property("active"));
    }
}
