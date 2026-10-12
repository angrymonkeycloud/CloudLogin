using System.Net;
using AngryMonkey.CloudLogin.Cli;

namespace AngryMonkey.CloudLogin.Tests.Cli;

public class CliSignInTests
{
    private static (BrowserSession Browser, ApplicationClient Application, NativeSignIn SignIn) Parts(FakeDeployment deployment) =>
        (new BrowserSession(new NonDisposingHandler(deployment.Transport), new CookieContainer(), TimeSpan.FromSeconds(10)),
         new ApplicationClient(FakeDeployment.App, new NonDisposingHandler(deployment.Transport), new CookieContainer(), TimeSpan.FromSeconds(10)),
         new NativeSignIn(FakeDeployment.App, deployment.Scheme));

    // ── The flow ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestModeSignIn_RedeemsTheHandoff_WithTheVerifierOfTheChallengeItSent()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        NativeSignInResult result = await signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application);

        Assert.Equal("Ada Lovelace", result.User.DisplayName);
        Assert.Equal(FakeDeployment.Handoff, result.Handoff);
        Assert.Equal(result.Verifier, deployment.Verifier);
        Assert.Contains(application.Cookies.GetAllCookies(), cookie => cookie.Name == FakeDeployment.SessionCookie);
    }

    [Fact]
    public async Task TheSessionCookie_LandsInTheApplication_NotInTheBrowser()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application);

        Assert.DoesNotContain(browser.Cookies.GetAllCookies(), cookie => cookie.Name == FakeDeployment.SessionCookie);
        Assert.Contains(browser.Cookies.GetAllCookies(), cookie => cookie.Name == "authority");
    }

    [Fact]
    public async Task TheVerifier_NeverTravelsThroughTheBrowser()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        NativeSignInResult result = await signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application);

        Assert.All(deployment.Transport.Requests.Where(request => !request.Uri.AbsolutePath.EndsWith("/auth/native/exchange", StringComparison.Ordinal)),
            request => Assert.DoesNotContain(result.Verifier, request.Uri.ToString() + request.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACallbackWithADifferentState_IsRefused_AndNothingIsRedeemed()
    {
        FakeDeployment deployment = new() { ReturnState = CloudLoginPkce.CreateState() };
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application));

        Assert.Contains("state", error.Message);
        Assert.Equal(0, deployment.Transport.Count(HttpMethod.Post, "app.test/auth/native/exchange"));
    }

    [Fact]
    public async Task ACallbackOnAnotherScheme_IsRefused()
    {
        FakeDeployment deployment = new() { Scheme = "otherapp" };
        (BrowserSession browser, ApplicationClient application, _) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => new NativeSignIn(FakeDeployment.App, "blusky").RunAsync(new TestModeSignInDriver(null, false), browser, application));

        Assert.Contains("did not return to blusky://auth/callback", error.Message);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("access_denied")]
    public async Task ABackendError_IsReported(string code)
    {
        FakeDeployment deployment = new() { CallbackError = code };
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application));

        Assert.Contains(code, error.Message);
    }

    [Fact]
    public async Task ARefusedExchange_IsReported()
    {
        FakeDeployment deployment = new() { ExchangeStatus = HttpStatusCode.BadRequest };
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application));

        Assert.Contains("refused to redeem", error.Message);
    }

    [Fact]
    public async Task EachSignIn_StartsWithItsOwnStateAndChallenge()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application);
        await signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application);

        RecordedRequest[] starts = [.. deployment.Transport.Requests.Where(request => request.Uri.AbsolutePath == "/auth/native/login")];
        Assert.Equal(2, starts.Length);
        Assert.NotEqual(starts[0].Query("state"), starts[1].Query("state"));
        Assert.NotEqual(starts[0].Query("challenge"), starts[1].Query("challenge"));
        Assert.Equal("blusky://auth/callback", starts[0].Query("redirect_uri"));
    }

    // ── Opening the authority's page ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ABackendThatDoesNotSendTheBrowserToCloudLogin_IsReported()
    {
        FakeDeployment deployment = new();
        deployment.Transport.On(HttpMethod.Get, "elsewhere.test/", HttpStatusCode.OK);
        using BrowserSession browser = new(new NonDisposingHandler(deployment.Transport), new CookieContainer(), TimeSpan.FromSeconds(5));

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => AuthorityPage.OpenAsync(browser, "https://elsewhere.test/", CancellationToken.None));

        Assert.Contains("did not send the browser to a CloudLogin sign-in page", error.Message);
    }

    [Fact]
    public async Task ASignInPageThatFails_IsReported()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/native/login", HttpStatusCode.InternalServerError);
        using BrowserSession browser = new(new NonDisposingHandler(transport), new CookieContainer(), TimeSpan.FromSeconds(5));

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => AuthorityPage.OpenAsync(browser, "https://app.test/auth/native/login", CancellationToken.None));

        Assert.Contains("500", error.Message);
    }

    [Fact]
    public async Task ABackendThatReturnsToTheAppAtOnce_IsReported()
    {
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/native/login", _ => ScriptedTransport.Redirect("blusky://auth/callback?error=unavailable&state=s"));
        using BrowserSession browser = new(new NonDisposingHandler(transport), new CookieContainer(), TimeSpan.FromSeconds(5));

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => AuthorityPage.OpenAsync(browser, "https://app.test/auth/native/login", CancellationToken.None));

        Assert.Contains("error=unavailable", error.Message);
    }

    [Fact]
    public void TheCompleteUrl_CarriesTheReference() =>
        Assert.Equal("https://login.test/CloudLogin/Login/Complete?referer=cltx%3Aabc", new AuthorityPage(new Uri("https://login.test/?referer=cltx%3Aabc"), "cltx:abc").CompleteUrl);

    // ── Test Mode ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("ada lovelace")]
    public async Task TestMode_SignsInTheNamedUser(string name)
    {
        FakeDeployment deployment = new();
        deployment.TestUsers.Insert(0, new CloudUser { Id = Guid.NewGuid(), FirstName = "Grace", LastName = "Hopper" });
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new TestModeSignInDriver(name, false), browser, application);

        RecordedRequest testSignIn = deployment.Transport.Requests.Single(request => request.Uri.AbsolutePath == "/CloudLogin/Login/TestSignIn");
        Assert.Equal(deployment.TestUsers.Single(user => user.FirstName == "Ada").Id.ToString(), testSignIn.Form("userId"));
        Assert.Equal("false", testSignIn.Form("keepMeSignedIn"));
    }

    [Fact]
    public async Task TestMode_CanSelectAUserById()
    {
        FakeDeployment deployment = new();
        Guid id = deployment.TestUsers[0].Id;
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new TestModeSignInDriver(id.ToString(), false), browser, application);

        Assert.Equal(id.ToString(), deployment.Transport.Requests.Single(request => request.Uri.AbsolutePath == "/CloudLogin/Login/TestSignIn").Form("userId"));
    }

    [Fact]
    public async Task TestMode_AnUnknownUser_IsReported_WithoutCreatingOne()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new TestModeSignInDriver("Nobody Here", false), browser, application));

        Assert.Contains("No test user is named 'Nobody Here'", error.Message);
        Assert.Equal(0, deployment.Transport.Count(HttpMethod.Post, "login.test/CloudLogin/Login/PasswordRegistration"));
    }

    [Fact]
    public async Task TestMode_CreatesTheUserWhenAskedTo()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new TestModeSignInDriver("Katherine Johnson", true), browser, application);

        RecordedRequest created = deployment.Transport.Requests.Single(request => request.Uri.AbsolutePath == "/CloudLogin/Login/PasswordRegistration");
        Assert.Equal("Katherine", created.Form("firstName"));
        Assert.Equal("Johnson", created.Form("lastName"));
        Assert.Equal(string.Empty, created.Form("password"));
        Assert.Equal("EmailAddress", created.Form("inputFormat"));
        Assert.EndsWith("@cloudlogin.test", created.Form("input"));
    }

    [Fact]
    public async Task TestMode_ASingleNameGetsAPlaceholderSurname()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new TestModeSignInDriver("Hedy", true), browser, application);

        RecordedRequest created = deployment.Transport.Requests.Single(request => request.Uri.AbsolutePath == "/CloudLogin/Login/PasswordRegistration");
        Assert.Equal("Hedy", created.Form("firstName"));
        Assert.Equal("Test", created.Form("lastName"));
    }

    [Fact]
    public async Task TestMode_WithNoTestUsers_SaysHowToCreateOne()
    {
        FakeDeployment deployment = new();
        deployment.TestUsers.Clear();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application));

        Assert.Contains("--create-test-user", error.Message);
    }

    [Fact]
    public async Task TestMode_OnAnAuthorityWithoutIt_SaysWhatToUseInstead()
    {
        FakeDeployment deployment = new() { TestModeEnabled = false };
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new TestModeSignInDriver(null, false), browser, application));

        Assert.Contains("does not offer Test Mode", error.Message);
        Assert.Contains("--mode password", error.Message);
    }

    [Theory]
    [InlineData("Ada Lovelace", true)]
    [InlineData("ADA LOVELACE", true)]
    [InlineData("Ada", false)]
    [InlineData("Lovelace", false)]
    [InlineData("ada@example.com", true)]
    [InlineData("other@example.com", false)]
    public void TestMode_MatchesByNameOrEmail(string selector, bool matches)
    {
        CloudUser user = new() { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace", Inputs = [new CloudLoginInput { Input = "ada@example.com", Format = CloudLoginInputFormat.EmailAddress, IsPrimary = true }] };
        Assert.Equal(matches, TestModeSignInDriver.Matches(user, selector));
    }

    // ── Password ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Password_SignsIn_ThenCompletesTheReference_AsTheSignInPageDoes()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        await signIn.RunAsync(new PasswordSignInDriver("ada@example.com", deployment.ExpectedPassword), browser, application);

        RecordedRequest posted = deployment.Transport.Requests.Single(request => request.Uri.AbsolutePath == "/CloudLogin/Login/PasswordSignIn");
        Assert.Equal("ada@example.com", posted.Form("email"));
        Assert.Equal("false", posted.Form("keepMeSignedIn"));
        Assert.Null(posted.Form("referer"));

        RecordedRequest completed = deployment.Transport.Requests.Single(request => request.Uri.AbsolutePath == "/CloudLogin/Login/Complete");
        Assert.Equal("cltx:tx-1", completed.Query("referer"));
        Assert.True(deployment.Transport.Requests.IndexOf(posted) < deployment.Transport.Requests.IndexOf(completed));
    }

    [Fact]
    public async Task Password_AWrongPassword_IsReported()
    {
        FakeDeployment deployment = new();
        (BrowserSession browser, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => signIn.RunAsync(new PasswordSignInDriver("ada@example.com", "wrong"), browser, application));

        Assert.Contains("refused the password sign-in", error.Message);
        Assert.Contains("400", error.Message);
    }

    // ── Paste ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paste_PrintsTheAddress_OpensTheBrowser_AndUsesThePastedCallback()
    {
        FakeDeployment deployment = new();
        StringWriter output = new();
        List<string> opened = [];
        string? loginUrl = null;
        ComputedReader input = new(() => $"  blusky://auth/callback?handoff={FakeDeployment.Handoff}&state={Uri.EscapeDataString(new RecordedRequest(HttpMethod.Get, new Uri(loginUrl = opened.Single()), new Dictionary<string, string>(), string.Empty).Query("state")!)}  ");
        (_, ApplicationClient application, NativeSignIn signIn) = Parts(deployment);
        using BrowserSession unused = new(new NonDisposingHandler(deployment.Transport), new CookieContainer(), TimeSpan.FromSeconds(5));

        deployment.Transport.On(HttpMethod.Get, "app.test/auth/native/login", HttpStatusCode.OK);
        (string handoff, _, _) = await signIn.ReceiveHandoffAsync(new PasteSignInDriver(output, input, opened.Add), unused);

        Assert.Equal(FakeDeployment.Handoff, handoff);
        Assert.Contains(loginUrl!, output.ToString());
        Assert.StartsWith("https://app.test/auth/native/login?", loginUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Paste_NothingPasted_IsReported(string? pasted)
    {
        PasteSignInDriver driver = new(new StringWriter(), new ComputedReader(() => pasted), null);
        using BrowserSession browser = new(new NonDisposingHandler(new ScriptedTransport()), new CookieContainer(), TimeSpan.FromSeconds(5));

        CliSignInException error = await Assert.ThrowsAsync<CliSignInException>(() => driver.SignInAsync(browser, "https://app.test/auth/native/login?x"));

        Assert.Contains("No address was pasted", error.Message);
    }

    [Fact]
    public async Task Paste_WithoutABrowser_StillPrintsTheAddress()
    {
        StringWriter output = new();
        PasteSignInDriver driver = new(output, new ComputedReader(() => "blusky://auth/callback?handoff=h&state=s"), openBrowser: null);
        using BrowserSession browser = new(new NonDisposingHandler(new ScriptedTransport()), new CookieContainer(), TimeSpan.FromSeconds(5));

        string pasted = await driver.SignInAsync(browser, "https://app.test/auth/native/login?x=1");

        Assert.Equal("blusky://auth/callback?handoff=h&state=s", pasted);
        Assert.Contains("https://app.test/auth/native/login?x=1", output.ToString());
    }

    [Fact]
    public void TheDrivers_NameTheirModes()
    {
        Assert.Equal("testmode", new TestModeSignInDriver(null, false).Name);
        Assert.Equal("password", new PasswordSignInDriver("a", "b").Name);
        Assert.Equal("paste", new PasteSignInDriver(TextWriter.Null, TextReader.Null, null).Name);
    }
}
