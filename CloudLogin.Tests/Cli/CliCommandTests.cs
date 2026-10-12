using System.Net;
using System.Text.Json;
using AngryMonkey.CloudLogin.Cli;

namespace AngryMonkey.CloudLogin.Tests.Cli;

public class CliCommandTests
{
    private static string[] Login(params string[] extra) => ["login", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "testmode", .. extra];

    // ── Help and usage ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData]
    [InlineData("help")]
    [InlineData("--help")]
    public async Task Help_ListsEveryCommand(params string[] args)
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(new ScriptedTransport(), args));

        foreach (string command in new[] { "login", "status", "token", "call", "logout", "scenario" })
            Assert.Contains(command, cli.Out.ToString());
    }

    [Theory]
    [InlineData("login")]
    [InlineData("status")]
    [InlineData("token")]
    [InlineData("call")]
    [InlineData("logout")]
    [InlineData("scenario")]
    public async Task EachCommandHasItsOwnHelp(string command)
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(new ScriptedTransport(), command, "--help"));
        Assert.Contains($"cloudlogin {command}", cli.Out.ToString());
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(new ScriptedTransport(), "help", command));
    }

    [Fact]
    public async Task HelpForAnUnknownCommand_IsAUsageError()
    {
        using CliHarness cli = new();
        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "help", "nonsense"));
    }

    [Fact]
    public async Task HelpNeverTouchesTheNetwork()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new();

        await cli.RunAsync(transport, "login", "--help");

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Version_PrintsTheVersion()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(new ScriptedTransport(), "--version"));
        Assert.StartsWith("cloudlogin ", cli.Out.ToString());
    }

    [Fact]
    public async Task AnUnknownCommand_IsAUsageError_OnStandardError()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "frobnicate"));
        Assert.Contains("Unknown command", cli.Error.ToString());
        Assert.Equal(string.Empty, cli.Out.ToString());
    }

    [Fact]
    public async Task Login_WithoutAnApplication_SaysWhatIsMissing()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "login", "--scheme", "blusky"));
        Assert.Contains("--app", cli.Error.ToString());
    }

    [Fact]
    public async Task Login_TakesTheApplicationAndSchemeFromTheEnvironment()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        cli.Variables[CloudLoginCli.ApplicationVariable] = FakeDeployment.App;
        cli.Variables[CloudLoginCli.SchemeVariable] = "blusky";
        cli.Variables[CloudLoginCli.ModeVariable] = "testmode";

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "login"));
    }

    [Theory]
    [InlineData("ftp://app.test")]
    [InlineData("app.test")]
    [InlineData("https://app.test/?tenant=1")]
    [InlineData("https://app.test/#x")]
    [InlineData("http://app.test")]
    public async Task Login_RefusesAnAddressThatIsNotASafeApplication(string application)
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(transport, "login", "--app", application, "--scheme", "blusky", "--mode", "testmode"));
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData("http://localhost:5014")]
    [InlineData("http://127.0.0.1:5014")]
    public async Task Login_AcceptsPlainHttpOnThisComputerOnly(string application)
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new();

        await cli.RunAsync(transport, "login", "--app", application, "--scheme", "blusky", "--mode", "testmode");

        Assert.DoesNotContain("must use https", cli.Error.ToString());
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("1app")]
    [InlineData("bad scheme")]
    [InlineData("a_b")]
    public async Task Login_RefusesASchemeAnApplicationCouldNotRegister(string scheme)
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "login", "--app", FakeDeployment.App, "--scheme", scheme, "--mode", "testmode"));
    }

    [Theory]
    [InlineData("blusky:")]
    [InlineData("blusky://")]
    [InlineData("BluSky")]
    public async Task Login_ToleratesTheSchemeWrittenAsAPrefix(string scheme)
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "login", "--app", FakeDeployment.App, "--scheme", scheme, "--mode", "testmode"));
        Assert.Equal("blusky", (await cli.LoadSessionAsync()).Scheme);
    }

    [Fact]
    public async Task Login_AnUnknownMode_IsAUsageError()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "login", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "magic"));
        Assert.Contains("Unknown sign-in mode", cli.Error.ToString());
    }

    // ── login ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_SavesTheSession_AndSaysWhoSignedIn()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, Login()));

        CliSession session = await cli.LoadSessionAsync();
        Assert.Equal(FakeDeployment.App, session.ApplicationUrl);
        Assert.Equal("blusky", session.Scheme);
        Assert.Equal("Ada Lovelace", session.User!.DisplayName);
        Assert.Contains(session.ApplicationCookies, cookie => cookie.Name == FakeDeployment.SessionCookie && cookie.Value == "session-1");
        Assert.Contains(session.BrowserCookies, cookie => cookie.Name == "authority");
        Assert.Contains("Signed in as Ada Lovelace", cli.Out.ToString());
        Assert.Contains(cli.SessionPath, cli.Out.ToString());
    }

    [Fact]
    public async Task Login_TrailingSlashOnTheApplication_IsDropped()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();

        await cli.RunAsync(deployment.Transport, "login", "--app", FakeDeployment.App + "/", "--scheme", "blusky", "--mode", "testmode");

        Assert.Equal(FakeDeployment.App, (await cli.LoadSessionAsync()).ApplicationUrl);
    }

    [Fact]
    public async Task Login_ToAChosenSessionFile()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        string other = Path.Combine(Path.GetDirectoryName(cli.SessionPath)!, "other.json");

        await cli.RunAsync(deployment.Transport, Login("--session", other));

        Assert.True(File.Exists(other));
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Login_ASessionFileFromTheEnvironment()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        string other = Path.Combine(Path.GetDirectoryName(cli.SessionPath)!, "env.json");
        cli.Variables[CloudLoginCli.SessionVariable] = other;

        await cli.RunAsync(deployment.Transport, Login());

        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task Login_AFailedSignIn_SavesNothing_AndFails()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { ExchangeStatus = HttpStatusCode.BadRequest };

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, Login()));
        Assert.Contains("Sign-in failed", cli.Error.ToString());
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Login_AStateMismatch_SavesNothing()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { ReturnState = CloudLoginPkce.CreateState() };

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, Login()));
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Login_AnUnreachableServer_IsUnavailable()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new() { Fault = _ => new HttpRequestException("No such host is known.") };

        Assert.Equal(CliExitCodes.Unavailable, await cli.RunAsync(transport, Login()));
        Assert.Contains("Could not reach the server", cli.Error.ToString());
    }

    [Fact]
    public async Task Login_ATimeout_IsUnavailable()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new() { Fault = _ => new TaskCanceledException("timeout") };

        Assert.Equal(CliExitCodes.Unavailable, await cli.RunAsync(transport, Login()));
        Assert.Contains("did not answer in time", cli.Error.ToString());
    }

    [Fact]
    public async Task Login_Cancelled_SaysSo()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Equal(CliExitCodes.Failed, await CloudLoginCli.RunAsync(Login(), cli.Environment(deployment.Transport), cancelled.Token));
        Assert.Contains("Cancelled", cli.Error.ToString());
    }

    [Fact]
    public async Task Login_WithPassword_ReadsItFromTheEnvironment_NeverFromAnOption()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        cli.Variables[CloudLoginCli.PasswordVariable] = deployment.ExpectedPassword;

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "login", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "password", "--email", "ada@example.com"));
        Assert.DoesNotContain(deployment.ExpectedPassword, cli.Out.ToString() + cli.Error.ToString());
    }

    [Fact]
    public async Task Login_WithPassword_AsksForItWhenNotSet()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        cli.Input = new StringReader(deployment.ExpectedPassword + Environment.NewLine);

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "login", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "password", "--email", "ada@example.com"));
        Assert.Contains("Password:", cli.Error.ToString());
    }

    [Fact]
    public async Task Login_WithPassword_NoPassword_IsAUsageError()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "login", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "password", "--email", "ada@example.com"));
        Assert.Contains(CloudLoginCli.PasswordVariable, cli.Error.ToString());
    }

    [Fact]
    public async Task Login_WithPassword_NeedsAnEmail()
    {
        using CliHarness cli = new();
        cli.Variables[CloudLoginCli.PasswordVariable] = "x";

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "login", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "password"));
        Assert.Contains("--email", cli.Error.ToString());
    }

    [Fact]
    public async Task Login_ByDefault_PastesAndOpensTheBrowser()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        cli.Input = new ComputedReader(() => deployment.BrowserOpened(cli.Opened.Single()));

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "login", "--app", FakeDeployment.App, "--scheme", "blusky"));

        Assert.Single(cli.Opened);
        Assert.Contains("paste it here", cli.Out.ToString());
        Assert.Equal(0, deployment.Transport.Count(HttpMethod.Get, "app.test/auth/native/login"));
        Assert.Equal(1, deployment.Transport.Count(HttpMethod.Post, "app.test/auth/native/exchange"));
        Assert.Equal("Ada Lovelace", (await cli.LoadSessionAsync()).User!.DisplayName);
    }

    [Fact]
    public async Task Login_PastedCallbackFromAnotherSignIn_IsRefused()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { ReturnState = CloudLoginPkce.CreateState() };
        cli.Input = new ComputedReader(() => deployment.BrowserOpened(cli.Opened.Single()));

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, "login", "--app", FakeDeployment.App, "--scheme", "blusky"));
        Assert.Contains("state", cli.Error.ToString());
        Assert.Equal(0, deployment.Transport.Count(HttpMethod.Post, "app.test/auth/native/exchange"));
    }

    [Fact]
    public async Task Login_NoOpen_PrintsWithoutOpening()
    {
        using CliHarness cli = new();
        cli.Input = new StringReader(string.Empty);

        await cli.RunAsync(new ScriptedTransport(), "login", "--app", FakeDeployment.App, "--scheme", "blusky", "--no-open");

        Assert.Empty(cli.Opened);
        Assert.Contains("/auth/native/login?", cli.Out.ToString());
    }

    // ── status ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_NotSignedIn_Fails_AndSaysHowToSignIn()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(new ScriptedTransport(), "status"));
        Assert.Contains("cloudlogin login", cli.Error.ToString());
    }

    [Fact]
    public async Task Status_AValidSession_Succeeds()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "status"));
        Assert.Contains("The session is valid", cli.Out.ToString());
        Assert.Contains("lasts 600 s", cli.Out.ToString());
    }

    [Fact]
    public async Task Status_SendsTheSavedCookie_OnlyToTheApplication()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync(cookieValue: "saved-value");

        await cli.RunAsync(deployment.Transport, "status");

        RecordedRequest request = Assert.Single(deployment.Transport.Requests);
        Assert.Equal("app.test", request.Uri.Host);
        Assert.Contains("app.session=saved-value", request.Header("Cookie"));
    }

    [Fact]
    public async Task Status_AnEndedSession_Fails_AndForgetsIt()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { TokenStatus = HttpStatusCode.Forbidden };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, "status"));
        Assert.Contains("has ended", cli.Error.ToString());
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Status_AnUnreachableAuthority_KeepsTheSession()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { TokenStatus = HttpStatusCode.ServiceUnavailable };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Unavailable, await cli.RunAsync(deployment.Transport, "status"));
        Assert.True(File.Exists(cli.SessionPath));
        Assert.Contains("kept", cli.Error.ToString());
    }

    [Fact]
    public async Task Status_SavesARenewedCookie()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/token", _ =>
        {
            HttpResponseMessage response = ScriptedTransport.Respond(HttpStatusCode.OK, """{"access_token":"a","expires_in":600,"audience":"app"}""");
            response.Headers.Add("Set-Cookie", "app.session=renewed; path=/; secure; httponly");
            return response;
        });
        await cli.SaveSessionAsync(cookieValue: "original");

        await cli.RunAsync(transport, "status");

        Assert.Equal("renewed", (await cli.LoadSessionAsync()).ApplicationCookies.Single(cookie => cookie.Name == "app.session").Value);
    }

    [Fact]
    public async Task Status_Json_IsMachineReadable()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        await cli.RunAsync(deployment.Transport, "status", "--json");

        using JsonDocument json = JsonDocument.Parse(cli.Out.ToString());
        Assert.Equal("Issued", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(FakeDeployment.App, json.RootElement.GetProperty("application").GetString());
    }

    [Fact]
    public async Task Status_AnUnreadableSession_Fails_AndSaysToSignInAgain()
    {
        using CliHarness cli = new();
        await cli.SaveSessionAsync();
        cli.Protector = new FilePermissionSessionProtector();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(new ScriptedTransport(), "status"));
        Assert.Contains("could not be read", cli.Error.ToString());
    }

    // ── token ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_ShowsClaims_ButNotTheTokenUnlessAsked()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "token", "--audience", "portal"));

        string output = cli.Out.ToString();
        Assert.Contains("Token for portal", output);
        Assert.Contains("sub: user-1", output);
        Assert.DoesNotContain(FakeDeployment.Jwt(), output);
        Assert.Equal("portal", deployment.Transport.Requests.Single().Query("audience"));
    }

    [Fact]
    public async Task Token_ShowToken_PrintsIt()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        await cli.RunAsync(deployment.Transport, "token", "--show-token");

        Assert.Contains(FakeDeployment.Jwt(), cli.Out.ToString());
    }

    [Fact]
    public async Task Token_Json_LeavesTheTokenOutUnlessAsked()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        await cli.RunAsync(deployment.Transport, "token", "--json");

        using JsonDocument json = JsonDocument.Parse(cli.Out.ToString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("accessToken").ValueKind);
        Assert.Equal("user-1", json.RootElement.GetProperty("claims").GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Token_AnEndedSession_Fails()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { TokenStatus = HttpStatusCode.Unauthorized };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, "token"));
    }

    // ── call ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Call_SendsThePathWithTheSession_AndPrintsTheAnswer()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/api/profile", HttpStatusCode.OK, """{"name":"Ada"}""");
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(transport, "call", "/api/profile"));

        Assert.Contains("HTTP 200", cli.Out.ToString());
        Assert.Contains("\"name\":\"Ada\"", cli.Out.ToString());
        Assert.Contains("app.session=", transport.Requests.Single().Header("Cookie"));
    }

    [Fact]
    public async Task Call_WithABody_PostsJson()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Post, "app.test/api/save", HttpStatusCode.OK);
        await cli.SaveSessionAsync();

        await cli.RunAsync(transport, "call", "api/save", "--body", """{"x":1}""");

        RecordedRequest request = transport.Requests.Single();
        Assert.Equal("""{"x":1}""", request.Body);
        Assert.StartsWith("application/json", request.Header("Content-Type"));
    }

    [Theory]
    [InlineData("delete", "DELETE")]
    [InlineData("PUT", "PUT")]
    public async Task Call_UsesTheGivenMethod(string option, string expected)
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(new HttpMethod(expected), "app.test/api/item", HttpStatusCode.NoContent);
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(transport, "call", "api/item", "--method", option));
    }

    [Fact]
    public async Task Call_AnUnsupportedMethod_IsAUsageError()
    {
        using CliHarness cli = new();
        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "call", "api/x", "--method", "TRACE"));
    }

    [Fact]
    public async Task Call_NeverSendsTheSessionToAnotherAddress()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new();
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(transport, "call", "https://attacker.example/steal"));
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData]
    [InlineData("a", "b")]
    public async Task Call_NeedsExactlyOnePath(params string[] paths)
    {
        using CliHarness cli = new();
        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), ["call", .. paths]));
    }

    [Fact]
    public async Task Call_AnErrorAnswer_Fails_ButKeepsTheSession()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/api/x", HttpStatusCode.InternalServerError, "boom");
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(transport, "call", "api/x"));
        Assert.True(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Call_A401_EndsTheSavedSession()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/api/x", HttpStatusCode.Unauthorized);
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(transport, "call", "api/x"));
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Call_ALongAnswer_IsShortened()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/api/big", HttpStatusCode.OK, new string('x', 20000));
        await cli.SaveSessionAsync();

        await cli.RunAsync(transport, "call", "api/big");

        Assert.True(cli.Out.ToString().Length < 9000);
    }

    // ── logout ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_EndsTheSession_AndForgetsIt()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "logout"));

        Assert.Contains("Signed out", cli.Out.ToString());
        Assert.False(File.Exists(cli.SessionPath));
        RecordedRequest request = deployment.Transport.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/auth/native/logout", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Logout_NeedsNoBrowser()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();
        await cli.SaveSessionAsync();

        await cli.RunAsync(deployment.Transport, "logout");

        Assert.Empty(cli.Opened);
        Assert.DoesNotContain(deployment.Transport.Requests, request => request.Uri.Host == "login.test");
    }

    [Fact]
    public async Task Logout_WhenNothingIsLeft_StillSucceeds_AndForgets()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { SessionEndStatus = HttpStatusCode.Unauthorized };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "logout"));
        Assert.Contains("no session left", cli.Out.ToString());
        Assert.False(File.Exists(cli.SessionPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Logout_WhenTheServerCannotEndIt_KeepsTheSessionToTryAgain(HttpStatusCode status)
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { SessionEndStatus = status };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Unavailable, await cli.RunAsync(deployment.Transport, "logout"));
        Assert.True(File.Exists(cli.SessionPath));
        Assert.Contains("again", cli.Error.ToString());
    }

    [Fact]
    public async Task Logout_WhenOffline_KeepsTheSessionToTryAgain()
    {
        using CliHarness cli = new();
        ScriptedTransport transport = new() { Fault = _ => new HttpRequestException("offline") };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Unavailable, await cli.RunAsync(transport, "logout"));
        Assert.True(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Logout_Refused_Fails_AndKeepsTheSession()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new() { SessionEndStatus = HttpStatusCode.Forbidden };
        await cli.SaveSessionAsync();

        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, "logout"));
        Assert.True(File.Exists(cli.SessionPath));
    }

    [Fact]
    public async Task Logout_NotSignedIn_Fails()
    {
        using CliHarness cli = new();
        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(new ScriptedTransport(), "logout"));
    }

    // ── A whole session ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginStatusTokenLogout_InSequence()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();

        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, Login()));
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "status"));
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "token"));
        Assert.Equal(CliExitCodes.Success, await cli.RunAsync(deployment.Transport, "logout"));
        Assert.Equal(CliExitCodes.Failed, await cli.RunAsync(deployment.Transport, "status"));
    }

    // ── scenario usage ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scenario_RefusesPasteMode()
    {
        using CliHarness cli = new();

        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "scenario", "--app", FakeDeployment.App, "--scheme", "blusky", "--mode", "paste"));
        Assert.Contains("--mode testmode", cli.Error.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    public async Task Scenario_ParallelMustBeSensible(string parallel)
    {
        using CliHarness cli = new();
        Assert.Equal(CliExitCodes.Usage, await cli.RunAsync(new ScriptedTransport(), "scenario", "--app", FakeDeployment.App, "--scheme", "blusky", "--parallel", parallel));
    }

    [Fact]
    public async Task Scenario_DefaultsToTestMode()
    {
        using CliHarness cli = new();
        FakeDeployment deployment = new();

        await cli.RunAsync(deployment.Transport, "scenario", "--app", FakeDeployment.App, "--scheme", "blusky");

        Assert.Contains("sign-in: testmode", cli.Out.ToString());
    }

    [Theory]
    [InlineData(null, "an unknown person")]
    public void Describe_WithoutAUser(CliUser? user, string expected) => Assert.Equal(expected, CloudLoginCli.Describe(user));

    [Fact]
    public void Describe_PrefersNameAndEmail()
    {
        Guid id = Guid.NewGuid();

        Assert.Equal("Ada (ada@example.com)", CloudLoginCli.Describe(new CliUser(id, "Ada", "ada@example.com")));
        Assert.Equal("Ada", CloudLoginCli.Describe(new CliUser(id, "Ada", null)));
        Assert.Equal("ada@example.com", CloudLoginCli.Describe(new CliUser(id, null, "ada@example.com")));
        Assert.Equal(id.ToString(), CloudLoginCli.Describe(new CliUser(id, null, null)));
    }
}
