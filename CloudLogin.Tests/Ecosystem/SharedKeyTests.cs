using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>Several websites with a backend on one secret key: each is still itself, is told of sign-outs where it said, and can be stopped on its own.</summary>
public class SharedKeyTests
{
    private const string SharedKey = "one-key-shared-by-every-website-of-this-apphost";

    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost Shop, ConsumerHost Blog)> StartAsync()
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost shop = await ecosystem.AddConsumerAsync("shop", secret: SharedKey);
        ConsumerHost blog = await ecosystem.AddConsumerAsync("blog", secret: SharedKey);
        return (ecosystem, shop, blog);
    }

    private static async Task<TestBrowser> SignInBothAsync(AuthEcosystem ecosystem, ConsumerHost shop, ConsumerHost blog)
    {
        TestBrowser browser = ecosystem.NewBrowser();
        await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser);
        await EcosystemFlow.LoginAsync(browser, shop, authorityAlreadySignedIn: true);
        await EcosystemFlow.LoginAsync(browser, blog, authorityAlreadySignedIn: true);

        Assert.True((await EcosystemFlow.MeAsync(browser, shop)).Authenticated);
        Assert.True((await EcosystemFlow.MeAsync(browser, blog)).Authenticated);
        return browser;
    }

    [Fact]
    public async Task TwoWebsitesOnOneKey_AreEachRecordedUnderTheirOwnName_WithTheBackChannelAddressTheyAnnounced()
    {
        (AuthEcosystem ecosystem, ConsumerHost shop, ConsumerHost blog) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        await SignInBothAsync(ecosystem, shop, blog);

        ApplicationDocument shopRecord = ecosystem.Authority.Applications.Documents["shop"];
        ApplicationDocument blogRecord = ecosystem.Authority.Applications.Documents["blog"];

        Assert.Equal([shop.Origin], shopRecord.Origins);
        Assert.Equal($"{shop.Origin}/auth/backchannel-logout", shopRecord.BackChannelLogoutUri);
        Assert.Equal($"{blog.Origin}/auth/backchannel-logout", blogRecord.BackChannelLogoutUri);
        Assert.Equal(shopRecord.LastSecretKeyId, blogRecord.LastSecretKeyId);
        Assert.Contains(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>(), family => family.ClientId == "shop");
        Assert.Contains(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>(), family => family.ClientId == "blog");
    }

    [Fact]
    public async Task SigningOutOfOne_ReachesTheOtherAtTheAddressItAnnounced()
    {
        (AuthEcosystem ecosystem, ConsumerHost shop, ConsumerHost blog) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = await SignInBothAsync(ecosystem, shop, blog);

        await browser.NavigateAsync($"{shop.Origin}/auth/logout?returnUrl=/", initiatorHost: shop.Host);

        Assert.False((await EcosystemFlow.MeAsync(browser, shop)).Authenticated);
        Assert.False((await EcosystemFlow.MeAsync(browser, blog)).Authenticated);
        Assert.Contains(ecosystem.Authority.Audit.Events, item => item.EventType == AuditEventTypes.LogoutNotified && item.ClientId == "blog");
        Assert.Equal(0, ecosystem.Authority.BackChannel.Failed);
    }

    [Theory]
    [InlineData("https://evil.test/auth/backchannel-logout")]
    [InlineData("https://shop.test.evil.test/auth/backchannel-logout")]
    [InlineData("http://shop.test/auth/backchannel-logout")]
    public async Task ABackChannelAddressOffTheReturnOrigin_IsRefused_AndNotRecorded(string backChannel)
    {
        (AuthEcosystem ecosystem, ConsumerHost shop, _) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;

        ApiResult begun = await new AuthorityClient(ecosystem.Network).BeginAsync(AuthorityClient.Basic("shop", SharedKey), $"{shop.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()), backChannel);

        Assert.Equal(HttpStatusCode.BadRequest, begun.Status);
        Assert.False(ecosystem.Authority.Applications.Documents.TryGetValue("shop", out ApplicationDocument? record) && record.BackChannelLogoutUri == backChannel);
    }

    [Fact]
    public async Task BlockingOneWebsite_EndsItsSessionsOnly_AndRefusesItsNextSignIn()
    {
        (AuthEcosystem ecosystem, ConsumerHost shop, ConsumerHost blog) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        TestBrowser browser = await SignInBothAsync(ecosystem, shop, blog);

        await ecosystem.Authority.Admin.BlockAsync(Guid.NewGuid(), "shop", "compromised");

        Assert.DoesNotContain(ecosystem.Authority.Sessions.Documents.Values.OfType<SessionFamilyDocument>(), family => family.ClientId == "shop" && family.RevokedOn is null);
        Assert.True((await EcosystemFlow.MeAsync(browser, blog)).Authenticated);

        ApiResult refused = await new AuthorityClient(ecosystem.Network).BeginAsync(AuthorityClient.Basic("shop", SharedKey), $"{shop.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
    }

    [Fact]
    public async Task RevokingTheSharedKey_StopsEveryWebsiteOnIt_UntilTheyMoveToAnotherKey()
    {
        (AuthEcosystem ecosystem, ConsumerHost shop, ConsumerHost blog) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        AuthorityClient client = new(ecosystem.Network);
        string deploymentKey = ClientDirectory.DeploymentKeyId(Server.Tokens.CloudLoginTokenOptions.HashSecret(SharedKey));

        await ecosystem.Authority.Keys.RevokeAsync(Guid.NewGuid(), deploymentKey, "leaked");
        IssuedSecretKey replacement = await ecosystem.Authority.Keys.CreateAsync(Guid.NewGuid(), "Replacement", DateTimeOffset.UtcNow.AddDays(30));

        foreach (ConsumerHost app in new[] { shop, blog })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.BeginAsync(AuthorityClient.Basic(app.Name, SharedKey), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()))).Status);
            Assert.Equal(HttpStatusCode.OK, (await client.BeginAsync(AuthorityClient.Basic(app.Name, replacement.Secret), $"{app.Origin}/auth/callback", CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(CloudLoginPkce.CreateVerifier()))).Status);
        }
    }
}
