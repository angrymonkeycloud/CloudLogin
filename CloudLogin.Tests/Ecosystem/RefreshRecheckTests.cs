using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>Every refresh re-checks the backend: a valid key, the name the token was issued to, and an application that is not blocked.</summary>
public class RefreshRecheckTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost App)> StartAsync()
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        return (ecosystem, app);
    }

    private static AuthorityClient Client(AuthEcosystem ecosystem) => new(ecosystem.Network);

    [Fact]
    public async Task ARefreshTokenIsBoundToTheBackendThatReceivedIt_EvenWhenAnotherHoldsTheSameKey()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        await ecosystem.AddConsumerAsync("app-b", secret: app.Secret);
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, app.Secret);

        ApiResult anonymous = await Client(ecosystem).RefreshAsync(null, tokens.Refresh!);
        ApiResult otherBackend = await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-b", app.Secret), tokens.Refresh!);
        ApiResult wrongSecret = await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", "wrong-secret-wrong-secret-wrong-secret"), tokens.Refresh!);
        ApiResult owner = await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", app.Secret), tokens.Refresh!);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, otherBackend.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.Status);
        Assert.Equal(HttpStatusCode.OK, owner.Status);
    }

    [Fact]
    public async Task ABackendMustPresentAKeyToRefresh_ItsNameAloneIsNotEnough()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, app.Secret);

        ApiResult claimedNameOnly = await Client(ecosystem).RefreshAsync(null, tokens.Refresh!, clientId: "app-a");

        Assert.Equal(HttpStatusCode.Unauthorized, claimedNameOnly.Status);
    }

    [Fact]
    public async Task ABlockedApplication_CannotRefresh_AndItsSessionsStayEndedAfterUnblocking()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, app.Secret);
        string authorization = AuthorityClient.Basic("app-a", app.Secret);

        await ecosystem.Authority.Admin.BlockAsync(Admin, "app-a", "compromised");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(authorization, tokens.Refresh!)).Status);

        await ecosystem.Authority.Admin.UnblockAsync(Admin, "app-a");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(authorization, tokens.Refresh!)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Client(ecosystem).RefreshAsync(authorization, (await TokenSets.IssueAsync(ecosystem, app, app.Secret)).Refresh!)).Status);
    }

    [Fact]
    public async Task ARevokedKey_CannotRefresh_ButTheSameApplicationCanWithAnotherKey()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, app.Secret);
        IssuedSecretKey replacement = await ecosystem.Authority.Keys.CreateAsync(Admin, "Replacement", null);
        string deploymentKey = (await ecosystem.Authority.Keys.ListAsync()).Single(view => view.Document.IsDeployment).Document.Id;

        await ecosystem.Authority.Keys.RevokeAsync(Admin, deploymentKey, "leaked");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", app.Secret), tokens.Refresh!)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", replacement.Secret), tokens.Refresh!)).Status);
    }

    [Fact]
    public async Task RefreshTokenReuse_StillRevokesTheWholeFamily()
    {
        (AuthEcosystem ecosystem, ConsumerHost app) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, app.Secret);
        string authorization = AuthorityClient.Basic("app-a", app.Secret);

        ApiResult rotated = await Client(ecosystem).RefreshAsync(authorization, tokens.Refresh!);
        Assert.Equal(HttpStatusCode.OK, rotated.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(authorization, tokens.Refresh!)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(ecosystem).RefreshAsync(authorization, rotated.Property("refresh_token")!)).Status);
    }

    [Fact]
    public async Task EveryApplication_GetsTheAuthoritysRefreshLifetime()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync(options => options.RefreshTokenLifetime = TimeSpan.FromDays(2));
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TokenSet tokens = await TokenSets.IssueAsync(ecosystem, app, app.Secret);

        ApiResult refreshed = await Client(ecosystem).RefreshAsync(AuthorityClient.Basic("app-a", app.Secret), tokens.Refresh!);

        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        SessionTokenDocument newest = ecosystem.Authority.Sessions.Documents.Values.OfType<SessionTokenDocument>().OrderByDescending(token => token.CreatedOn).First();
        Assert.True(newest.ExpiresOn <= DateTimeOffset.UtcNow.AddDays(2).AddMinutes(1));
    }
}
