using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Web;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginServerLoginTests
{
    [Fact]
    public async Task PasswordLogin_WithValidPassword_SignsInAndUpdatesUser()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        DateTimeOffset beforeLogin = DateTimeOffset.UtcNow;

        bool result = await fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("  PERSON@EXAMPLE.COM ", "Valid#123", true));

        Assert.True(result);
        Assert.Equal(1, fixture.Authentication.SignInCount);
        Assert.Equal(1, fixture.Store.UpdateCount);
        Assert.NotNull(fixture.Authentication.SignedInPrincipal);
        Assert.Equal(user.ID.ToString(), fixture.Authentication.SignedInPrincipal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("person@example.com", fixture.Authentication.SignedInPrincipal.FindFirstValue(ClaimTypes.Email));
        Assert.True(fixture.Authentication.SignedInProperties!.IsPersistent);
        Assert.InRange(
            fixture.Authentication.SignedInProperties.ExpiresUtc!.Value,
            beforeLogin.Add(fixture.Configuration.LoginDuration).AddSeconds(-1),
            DateTimeOffset.UtcNow.Add(fixture.Configuration.LoginDuration));
        Assert.True(user.LastSignedIn >= beforeLogin);
    }

    [Fact]
    public async Task PasswordLogin_WithWrongPassword_DoesNotSignIn()
    {
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();

        bool result = await fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("person@example.com", "Wrong#123"));

        Assert.False(result);
        Assert.Equal(0, fixture.Authentication.SignInCount);
        Assert.Equal(0, fixture.Store.UpdateCount);
    }

    [Fact]
    public async Task PasswordLogin_WithBlankCredentials_RejectsInvalidRequest()
    {
        LoginTestFixture fixture = new();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("person@example.com", "")));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("", "Valid#123")));
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task PasswordLogin_DoesNotAuthenticateTestAccount()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        UserModel user = await fixture.AddPasswordUserAsync(isTest: true);
        user.Inputs[0].Providers.Add(new LoginProvider
        {
            Code = "Password",
            PasswordHash = await fixture.Server.HashPassword("Valid#123")
        });

        bool result = await fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("person@example.com", "Valid#123"));

        Assert.False(result);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task PasswordLogin_WithoutPersistence_CreatesSessionCookie()
    {
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();

        Assert.True(await fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("person@example.com", "Valid#123")));

        Assert.False(fixture.Authentication.SignedInProperties!.IsPersistent);
        Assert.Null(fixture.Authentication.SignedInProperties.ExpiresUtc);
    }

    [Fact]
    public async Task TestLogin_WhenEnabled_SignsInOnlyTestUser()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        UserModel testUser = await fixture.AddPasswordUserAsync(isTest: true);

        bool result = await fixture.Server.TestLogin(testUser.ID, keepMeSignedIn: true);

        Assert.True(result);
        Assert.Equal("TestMode", fixture.Authentication.SignedInPrincipal!.Identity!.AuthenticationType);
        Assert.Equal(testUser.ID.ToString(), fixture.Authentication.SignedInPrincipal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(fixture.Authentication.SignedInProperties!.IsPersistent);
        Assert.Equal(1, fixture.Store.UpdateCount);
    }

    [Fact]
    public async Task TestLogin_WhenDisabled_IsRejected()
    {
        LoginTestFixture fixture = new();
        UserModel testUser = await fixture.AddPasswordUserAsync(isTest: true);

        Assert.False(await fixture.Server.TestLogin(testUser.ID));
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task TestLogin_RejectsRegularAndUnknownUsers()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        UserModel regularUser = await fixture.AddPasswordUserAsync();

        Assert.False(await fixture.Server.TestLogin(regularUser.ID));
        Assert.False(await fixture.Server.TestLogin(Guid.NewGuid()));
        Assert.False(await fixture.Server.TestLogin(Guid.Empty));
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task TestLogin_EndToEnd_CompletesExternalRedirectOnFirstAttempt()
    {
        LoginTestFixture fixture = new(
            testModeEnabled: true,
            allowedOrigins: ["https://portal.example:7443"]);
        UserModel testUser = await fixture.AddPasswordUserAsync(isTest: true);

        Assert.True(await fixture.Server.TestLogin(testUser.ID));
        fixture.AuthenticateAs(testUser);

        string redirect = await fixture.Server.CompleteLoginRedirect(
            "https://portal.example:7443/auth/login?returnUrl=%2F");

        Uri uri = new(redirect);
        string? requestId = HttpUtility.ParseQueryString(uri.Query)["requestId"];
        Assert.True(Guid.TryParse(requestId, out Guid parsedRequestId));
        Assert.Equal(testUser.ID, fixture.Store.Requests[parsedRequestId]);
        Assert.Equal(1, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLoginRedirect_RequiresAuthenticatedUser()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Server.CompleteLoginRedirect("https://portal.example/auth/login"));
    }

    [Theory]
    [InlineData("https://attacker.example/callback")]
    [InlineData("//attacker.example/callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("unknownapp://auth/callback")]
    public async Task CompleteLoginRedirect_RejectsUnapprovedDestination(string destination)
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Server.CompleteLoginRedirect(destination));
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLoginRedirect_SameOrigin_DoesNotCreateRequest()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);

        string redirect = await fixture.Server.CompleteLoginRedirect(
            "https://login.example/Account?tab=security#passwords");

        Assert.Equal("https://login.example/Account?tab=security#passwords", redirect);
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLoginRedirect_SameHostDifferentPort_CreatesRequest()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://login.example:51671"]);
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);

        string redirect = await fixture.Server.CompleteLoginRedirect(
            "https://login.example:51671/auth/login?returnUrl=%2F#fragment");

        Assert.Contains("requestId=", redirect);
        Assert.EndsWith("#fragment", redirect);
        Assert.Equal(1, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLoginRedirect_AllowedMobileScheme_AddsRequestAndMobileFlag()
    {
        LoginTestFixture fixture = new(allowedMobileSchemes: ["blusky"]);
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);

        string redirect = await fixture.Server.CompleteLoginRedirect(
            "blusky://auth/callback?state=abc",
            isMobileApp: true);

        Uri uri = new(redirect);
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("abc", query["state"]);
        Assert.True(Guid.TryParse(query["requestId"], out _));
        Assert.Equal("true", query["isMobileApp"]);
        Assert.Equal(1, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLoginRedirect_EmptyDestination_GoesToAccount()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);

        Assert.Equal("/Account", await fixture.Server.CompleteLoginRedirect());
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task Login_ValidProvider_ReturnsChallengeWithProtectedState()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);

        IActionResult result = await fixture.Server.Login(
            "google",
            keepMeSignedIn: true,
            sameSite: false,
            referer: "https://portal.example/auth/login");

        ChallengeResult challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Contains(GoogleDefaults.AuthenticationScheme, challenge.AuthenticationSchemes);
        Assert.Equal("https://portal.example/auth/login", challenge.Properties!.Items["referer"]);
        Assert.Equal("True", challenge.Properties.Items["keepMeSignedIn"]);
    }

    [Fact]
    public async Task Login_RejectsUnapprovedRefererBeforeProviderChallenge()
    {
        LoginTestFixture fixture = new();

        IActionResult result = await fixture.Server.Login(
            "google", false, false, referer: "https://attacker.example/callback");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_UnknownProvider_ReturnsNotFound()
    {
        LoginTestFixture fixture = new();

        IActionResult result = await fixture.Server.Login("unknown", false, false);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Login_AlreadyAuthenticated_CompletesExternalHandoff()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);

        IActionResult result = await fixture.Server.Login(
            "google", false, false, referer: "https://portal.example/auth/login");

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("requestId=", redirect.Url);
        Assert.Equal(1, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CustomLogin_IsDisabledByDefault()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();

        IActionResult result = await fixture.Server.CustomLogin(user.ID, false, "/Account");

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task UpdateAuth_LegacyCallerControlledCookieEndpoint_IsGone()
    {
        LoginTestFixture fixture = new();

        IActionResult result = await fixture.Server.UpdateAuth("/Account", "untrusted-cookie");

        StatusCodeResult gone = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(410, gone.StatusCode);
    }

    [Fact]
    public async Task Logout_ValidatesRedirectAndSignsOut()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);

        IActionResult blocked = await fixture.Server.Logout("https://attacker.example");
        IActionResult allowed = await fixture.Server.Logout("https://portal.example/signed-out");

        Assert.IsType<BadRequestObjectResult>(blocked);
        RedirectResult redirect = Assert.IsType<RedirectResult>(allowed);
        Assert.Equal("https://portal.example/signed-out", redirect.Url);
        Assert.Equal(1, fixture.Authentication.SignOutCount);
    }

    [Fact]
    public async Task PasswordRegistration_CreatesNormalizedPasswordUser()
    {
        LoginTestFixture fixture = new();

        UserModel user = await fixture.Server.PasswordRegistration(
            PasswordRegistrationRequest.Create(
                "  NEW@EXAMPLE.COM ",
                InputFormat.EmailAddress,
                "Valid#123",
                "New",
                "Person",
                "New Person"));

        Assert.False(user.IsTest);
        Assert.Equal("new@example.com", user.PrimaryEmailAddress!.Input);
        LoginProvider passwordProvider = Assert.Single(user.Inputs[0].Providers, provider => provider.Code == "Password");
        Assert.NotEqual("Valid#123", passwordProvider.PasswordHash);
        Assert.True(await fixture.Server.PasswordLogin(
            PasswordLoginRequest.Create("new@example.com", "Valid#123")));
    }

    [Fact]
    public async Task PasswordRegistration_WithoutPassword_IsOnlyAllowedInTestMode()
    {
        PasswordRegistrationRequest request = PasswordRegistrationRequest.Create(
            "test@test.cloud", InputFormat.EmailAddress, null, "Test", "User", "Test User");
        LoginTestFixture regularFixture = new();
        LoginTestFixture testFixture = new(testModeEnabled: true);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            regularFixture.Server.PasswordRegistration(request));

        UserModel testUser = await testFixture.Server.PasswordRegistration(request);
        Assert.True(testUser.IsTest);
        Assert.Empty(testUser.Inputs[0].Providers);
        Assert.True(await testFixture.Server.TestLogin(testUser.ID));
    }
}
