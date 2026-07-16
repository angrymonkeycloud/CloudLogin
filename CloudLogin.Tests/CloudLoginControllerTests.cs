using AngryMonkey.CloudLogin.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginControllerTests
{
    [Fact]
    public async Task TestSignIn_ValidTestUser_ReturnsOk()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        UserModel user = await fixture.AddPasswordUserAsync(isTest: true);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.TestSignIn(user.ID, keepMeSignedIn: true);

        Assert.IsType<OkResult>(result);
        Assert.Equal(1, fixture.Authentication.SignInCount);
        Assert.True(fixture.Authentication.SignedInProperties!.IsPersistent);
    }

    [Fact]
    public async Task TestSignIn_InvalidUser_ReturnsUnauthorized()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.TestSignIn(Guid.NewGuid());

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task LegacyTestSignIn_ReloadsTestUserAndCompletesExternalHandoff()
    {
        LoginTestFixture fixture = new(
            testModeEnabled: true,
            allowedOrigins: ["https://portal.example"]);
        UserModel user = await fixture.AddPasswordUserAsync(isTest: true);
        LoginController controller = CreateLoginController(fixture);
        string legacyUserInfo = JsonSerializer.Serialize(new
        {
            ID = user.ID,
            DisplayName = "Caller-controlled value",
            IsTest = false,
            IsLocked = true
        });

        IActionResult result = await controller.CustomLogin(
            Guid.Empty,
            keepMeSignedIn: true,
            referer: "https://portal.example/auth/callback?state=abc",
            userInfo: legacyUserInfo);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("requestId=", redirect.Url);
        Assert.Equal(1, fixture.Authentication.SignInCount);
        Assert.True(fixture.Authentication.SignedInProperties!.IsPersistent);
        Assert.Single(fixture.Store.Requests);
        Assert.Equal(user.ID, fixture.Store.Requests.Single().Value);
    }

    [Fact]
    public async Task LegacyTestSignIn_RejectsSpoofedRegularUser()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        UserModel user = await fixture.AddPasswordUserAsync(isTest: false);
        LoginController controller = CreateLoginController(fixture);
        string legacyUserInfo = JsonSerializer.Serialize(new
        {
            ID = user.ID,
            IsTest = true
        });

        IActionResult result = await controller.CustomLogin(
            Guid.Empty,
            keepMeSignedIn: false,
            referer: "/Account",
            userInfo: legacyUserInfo);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task PasswordSignIn_WrongPassword_ReturnsGenericBadRequest()
    {
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.PasswordSignIn(
            "person@example.com", "Wrong#123456");

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid email or password.", badRequest.Value);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task PasswordSignIn_ValidPassword_ReturnsOk()
    {
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.PasswordSignIn(
            "person@example.com", "Valid#123456");

        Assert.IsType<OkResult>(result);
        Assert.Equal(1, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task CompleteLogin_AnonymousUser_ReturnsUnauthorized()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.CompleteLogin("https://portal.example/auth/login");

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task CompleteLogin_UnapprovedDestination_ReturnsBadRequest()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.CompleteLogin("https://attacker.example/callback");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLogin_ApprovedDestination_ReturnsOneTimeRedirect()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.CompleteLogin("https://portal.example/auth/login");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("requestId=", Assert.IsType<string>(ok.Value));
        Assert.Equal(1, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CreateRequest_AnonymousUser_ReturnsUnauthorized()
    {
        LoginTestFixture fixture = new();
        RequestController controller = CreateRequestController(fixture);

        IActionResult result = await controller.CreateRequest(Guid.NewGuid());

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CreateRequest_DifferentUser_ReturnsForbidden()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        RequestController controller = CreateRequestController(fixture);

        IActionResult result = await controller.CreateRequest(Guid.NewGuid());

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CreateRequest_CurrentUser_ReturnsRequestedIdentifier()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        RequestController controller = CreateRequestController(fixture);
        Guid requestedId = Guid.NewGuid();

        IActionResult result = await controller.CreateRequest(user.ID, requestedId);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(requestedId, Assert.IsType<Guid>(ok.Value));
        Assert.Equal(user.ID, fixture.Store.Requests[requestedId]);
    }

    [Fact]
    public async Task RequestExchange_NeverReturnsPasswordHash()
    {
        LoginTestFixture fixture = new();
        UserModel user = await fixture.AddPasswordUserAsync();
        string originalHash = user.Inputs[0].Providers.Single().PasswordHash!;
        Guid requestId = Guid.NewGuid();
        fixture.Store.Requests[requestId] = user.ID;
        RequestController controller = CreateRequestController(fixture);

        IActionResult result = await controller.GetUserByRequestId(requestId);

        UserModel response = Assert.IsType<UserModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(response.Inputs[0].Providers.Single().PasswordHash);
        Assert.Equal(originalHash, user.Inputs[0].Providers.Single().PasswordHash);
    }

    private static LoginController CreateLoginController(LoginTestFixture fixture) => new(
        fixture.Configuration,
        fixture.Server)
    {
        ControllerContext = new ControllerContext { HttpContext = fixture.HttpContext }
    };

    private static RequestController CreateRequestController(LoginTestFixture fixture) => new(
        fixture.Configuration,
        fixture.Server)
    {
        ControllerContext = new ControllerContext { HttpContext = fixture.HttpContext }
    };
}
