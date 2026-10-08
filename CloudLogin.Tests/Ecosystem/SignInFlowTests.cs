using System.Net;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

public class SignInFlowTests
{
    [Fact]
    public async Task ASignIn_ThroughTheAuthority_CreatesAConsumerSessionBoundToTheAuthoritySession()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();

        BrowserResponse landed = await EcosystemFlow.LoginAsync(browser, app);

        Assert.True(landed.FinalUrl.AbsolutePath == "/home", $"{landed.Status} {landed.FinalUrl} :: {landed.Body} :: {string.Join(",", landed.Statuses)}");

        (bool authenticated, string? sessionId) = await EcosystemFlow.MeAsync(browser, app);
        Assert.True(authenticated);
        Assert.False(string.IsNullOrEmpty(sessionId));
    }
}
