using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

public class SessionOwnershipTests
{
    private const string AuthorityHostName = "login.test";

    private static string Cookie(TestBrowser browser) => string.Join("; ", browser.Cookies.Where(cookie => cookie.Host == AuthorityHostName).Select(cookie => $"{cookie.Name}={cookie.Value}"));

    private static async Task<(string SessionId, string FamilyId)> BrowserSessionAsync(AuthEcosystem ecosystem, Guid userId)
    {
        await Task.Yield();
        SessionFamilyDocument family = ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Single(item => item.UserId == userId.ToString() && item.Audience == SessionService.BrowserAudience && !item.IsRevoked);
        return (family.SessionId, family.FamilyId);
    }

    private static Task<ApiResult> RevokeAsync(AuthEcosystem ecosystem, string sessionId, string? bearer = null, string? cookie = null) =>
        new AuthorityClient(ecosystem.Network).PostAsync("/CloudLogin/Token/Revoke", new { session_id = sessionId }, bearer is null ? null : $"Bearer {bearer}", cookie);

    private static bool Active(AuthEcosystem ecosystem, string sessionId) =>
        ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Any(family => family.SessionId == sessionId && !family.IsRevoked);

    [Fact]
    public async Task AnAnonymousCaller_CannotRevokeAnySession()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        TestBrowser owner = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(owner);
        (string sessionId, _) = await BrowserSessionAsync(ecosystem, ecosystem.User.Id);

        ApiResult result = await RevokeAsync(ecosystem, sessionId);

        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.True(Active(ecosystem, sessionId));
    }

    [Fact]
    public async Task TheOwner_CanRevokeTheirOwnSession_AndItIsReallyEnded()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        TestBrowser owner = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(owner);
        (string sessionId, _) = await BrowserSessionAsync(ecosystem, ecosystem.User.Id);

        ApiResult result = await RevokeAsync(ecosystem, sessionId, cookie: Cookie(owner));

        Assert.Equal(HttpStatusCode.NoContent, result.Status);
        Assert.False(Active(ecosystem, sessionId));
    }

    [Fact]
    public async Task AnUnrelatedSignedInUser_CannotRevokeSomeoneElsesSession_AndLearnsNothingFromTheRefusal()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        CloudUser other = ecosystem.AddUser("other@example.com");
        TestBrowser owner = ecosystem.NewBrowser();
        TestBrowser stranger = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(owner);
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(stranger, "other@example.com");
        (string sessionId, _) = await BrowserSessionAsync(ecosystem, ecosystem.User.Id);

        ApiResult refused = await RevokeAsync(ecosystem, sessionId, cookie: Cookie(stranger));
        ApiResult unknown = await RevokeAsync(ecosystem, "sess_does_not_exist", cookie: Cookie(stranger));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal(refused.Status, unknown.Status);
        Assert.Equal(refused.Body, unknown.Body);
        Assert.DoesNotContain(sessionId, refused.Body);
        Assert.True(Active(ecosystem, sessionId));
        _ = other;
    }

    [Fact]
    public async Task ABearerTokenOfTheSameSession_CanRevokeIt_AndAnInvalidOneCannot()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, "secret-a");

        Assert.Equal(HttpStatusCode.Unauthorized, (await RevokeAsync(ecosystem, tokens.SessionId, bearer: "not.a.token")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RevokeAsync(ecosystem, tokens.SessionId, bearer: tokens.Access + "x")).Status);
        Assert.True(Active(ecosystem, tokens.SessionId));

        Assert.Equal(HttpStatusCode.NoContent, (await RevokeAsync(ecosystem, tokens.SessionId, bearer: tokens.Access)).Status);
        Assert.False(Active(ecosystem, tokens.SessionId));
    }

    [Fact]
    public async Task ABearerTokenOfAnotherUsersSession_CannotRevokeThisOne()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ecosystem.AddUser("other@example.com");
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet mine = await TokenSets.IssueAsync(ecosystem, app, "secret-a");
        TokenSet theirs = await TokenSets.IssueAsync(ecosystem, app, "secret-a", email: "other@example.com");

        ApiResult result = await RevokeAsync(ecosystem, mine.SessionId, bearer: theirs.Access);

        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.True(Active(ecosystem, mine.SessionId));
        Assert.True(Active(ecosystem, theirs.SessionId));
    }

    [Fact]
    public async Task AdministratorsRevokeThroughTheirOwnAuthorizedOperation_NotThroughTheUserEndpoint()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", secret: "secret-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, "secret-a");

        AdminSessionService admin = new(
            ecosystem.Authority.Sessions,
            ecosystem.Authority.SessionService,
            new Tests.Core.InMemoryUserRepository(),
            ecosystem.Authority.Admin,
            ecosystem.Authority.Services.GetRequiredService<IAuditLogger>());

        SessionFamilyDocument family = ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().First(item => item.SessionId == tokens.SessionId && item.Audience == "app-a");
        await admin.RevokeAsync(Guid.NewGuid(), family.FamilyId);

        Assert.True(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>().Single(item => item.FamilyId == family.FamilyId).IsRevoked);
        Assert.Contains(ecosystem.Authority.Audit.Events, item => item.EventType == AuditEventTypes.SessionRevoked);
    }
}
