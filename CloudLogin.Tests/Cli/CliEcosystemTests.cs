using AngryMonkey.CloudLogin.Cli;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Tests.Ecosystem;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.Cli;

/// <summary>The CLI against a real authority and a real application backend, as separate servers on the test network.</summary>
public class CliEcosystemTests
{
    private const string Scheme = "blusky";
    private const string RefreshEndpoint = "POST https://login.test/CloudLogin/Token/Refresh";

    private static async Task<(AuthEcosystem Ecosystem, ConsumerHost Backend)> StartAsync(TimeSpan? accessTokenLifetime = null, bool testMode = false)
    {
        AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync(options =>
        {
            if (accessTokenLifetime is { } lifetime)
                options.AccessTokenLifetime = lifetime;
        });

        ConsumerHost backend = await ecosystem.AddConsumerAsync(
            "blusky-app",
            secret: "secret-app",
            configureServer: configuration => configuration.NativeCallbackSchemes.Add(Scheme),
            configureServices: (services, configuration) => services.AddCloudLoginTokenAuthentication(configuration));

        if (testMode)
            ecosystem.Authority.Services.GetRequiredService<CloudLoginWebConfiguration>().AddTestMode();

        return (ecosystem, backend);
    }

    private static CliHarness Harness()
    {
        CliHarness cli = new();
        cli.Variables[CloudLoginCli.PasswordVariable] = AuthEcosystem.Password;
        return cli;
    }

    private static string[] PasswordLogin(ConsumerHost backend) =>
        ["login", "--app", backend.Origin, "--scheme", Scheme, "--mode", "password", "--email", AuthEcosystem.AuthorityEmail];

    private static string[] Scenario(ConsumerHost backend, params string[] extra) =>
        ["scenario", "--app", backend.Origin, "--scheme", Scheme, "--mode", "password", "--email", AuthEcosystem.AuthorityEmail, .. extra];

    [Fact]
    public async Task ASessionsWholeLife_ThroughTheCli()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, PasswordLogin(backend)));
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "status"));
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "token"));
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "call", "me"));
        Assert.Contains("\"authenticated\":true", cli.Out.ToString());

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "logout"));
        Assert.Contains("Signed out", cli.Out.ToString());
        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(ecosystem.Network, "status"));
    }

    [Fact]
    public async Task TheSavedCookie_StopsWorkingAfterLogout()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();
        await cli.RunAsync(ecosystem.Network, PasswordLogin(backend));
        CliSession saved = await cli.LoadSessionAsync();

        await cli.RunAsync(ecosystem.Network, "logout");
        await cli.Store.SaveAsync(saved);

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(ecosystem.Network, "status"));
    }

    [Fact]
    public async Task TheCli_SignsInTheSamePersonTheAuthorityKnows()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();

        await cli.RunAsync(ecosystem.Network, PasswordLogin(backend));

        Assert.Equal(ecosystem.User.Id, (await cli.LoadSessionAsync()).User!.Id);
    }

    [Fact]
    public async Task AWrongPassword_SavesNothing()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();
        cli.Variables[CloudLoginCli.PasswordVariable] = "Wrong#123456";

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(ecosystem.Network, PasswordLogin(backend)));
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task ABackendThatDoesNotListTheScheme_RefusesTheSignIn()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();

        Assert.NotEqual(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "login", "--app", backend.Origin, "--scheme", "otherapp", "--mode", "password", "--email", AuthEcosystem.AuthorityEmail));
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task TheScenario_PassesEveryStep_AgainstACorrectDeployment()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();

        int exit = await cli.RunAsync(ecosystem.Network, Scenario(backend));

        Assert.True(exit == CliExitCodes.Success, cli.Out.ToString() + cli.Error);
        Assert.Contains("14 passed, 0 failed, 0 skipped.", cli.Out.ToString());
    }

    [Fact]
    public async Task TheScenario_RenewsUnderLoad_WhenEveryRequestMustRefresh()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync(TimeSpan.FromSeconds(30));
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();

        int exit = await cli.RunAsync(ecosystem.Network, Scenario(backend, "--parallel", "16"));

        Assert.True(exit == CliExitCodes.Success, cli.Out.ToString() + cli.Error);
        Assert.Contains("accepted after the cookie was renewed", cli.Out.ToString());

        lock (ecosystem.Network.Requests)
            Assert.Contains(RefreshEndpoint, ecosystem.Network.Requests);
    }

    [Fact]
    public async Task TheScenario_Json_ReportsEachStep()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();

        await cli.RunAsync(ecosystem.Network, Scenario(backend, "--json"));

        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(cli.Out.ToString());
        Assert.Equal(14, json.RootElement.GetProperty("passed").GetInt32());
        Assert.Equal(14, json.RootElement.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task TheScenario_FailsLoudly_WhenTheAuthorityIsDown()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();
        ecosystem.Network.Routes.Remove("login.test");

        int exit = await cli.RunAsync(ecosystem.Network, Scenario(backend));

        Assert.Equal(CliExitCodes.Failed, exit);
        Assert.Contains("FAIL", cli.Out.ToString());
        Assert.Contains("SKIP", cli.Out.ToString());
    }

    [Fact]
    public async Task TestMode_SignsInWithNoPassword_AndCreatesTheUserWhenAsked()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync(testMode: true);
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = new();

        int exit = await cli.RunAsync(ecosystem.Network, "login", "--app", backend.Origin, "--scheme", Scheme, "--mode", "testmode", "--test-user", "Grace Hopper", "--create-test-user");

        Assert.True(exit == CliExitCodes.Success, cli.Error.ToString());
        Assert.Equal("Grace Hopper", (await cli.LoadSessionAsync()).User!.DisplayName);
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "status"));
    }

    [Fact]
    public async Task TestMode_TheScenarioPasses()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync(testMode: true);
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = new();

        int exit = await cli.RunAsync(ecosystem.Network, "scenario", "--app", backend.Origin, "--scheme", Scheme, "--mode", "testmode", "--test-user", "Grace Hopper", "--create-test-user");

        Assert.True(exit == CliExitCodes.Success, cli.Out.ToString() + cli.Error);
    }

    [Fact]
    public async Task TestMode_IsRefused_WhereTheAuthorityDoesNotEnableIt()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(ecosystem.Network, "login", "--app", backend.Origin, "--scheme", Scheme, "--mode", "testmode", "--create-test-user"));
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Status_RenewsAnExpiringSession_AndSavesTheRenewal()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync(TimeSpan.FromSeconds(30));
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();
        await cli.RunAsync(ecosystem.Network, PasswordLogin(backend));
        string before = (await cli.LoadSessionAsync()).ApplicationCookies.Single().Value;

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "status"));
        Assert.NotEqual(before, (await cli.LoadSessionAsync()).ApplicationCookies.Single().Value);
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "status"));
    }

    [Fact]
    public async Task Logout_WhenTheAuthorityIsDown_KeepsTheSession_AndFinishesLater()
    {
        (AuthEcosystem ecosystem, ConsumerHost backend) = await StartAsync();
        await using AuthEcosystem owner_ = ecosystem;
        using CliHarness cli = Harness();
        await cli.RunAsync(ecosystem.Network, PasswordLogin(backend));
        HttpMessageHandler authority = ecosystem.Network.Routes["login.test"];
        ecosystem.Network.Routes.Remove("login.test");

        Assert.Equal(CliExitCodes.Unavailable, await cli.RunAsync(ecosystem.Network, "logout"));
        Assert.True(File.Exists(cli.SessionPath));

        ecosystem.Network.Routes["login.test"] = authority;

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(ecosystem.Network, "logout"));
        Assert.False(File.Exists(cli.SessionPath));
    }
}
