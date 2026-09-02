using AngryMonkey.CloudLogin.API.Controllers;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginControllerTests
{
    [Fact]
    public void LoginResult_DoesNotAcceptCallerSuppliedUserData()
    {
        MethodInfo action = typeof(AngryMonkey.CloudLogin.API.CloudLoginController)
            .GetMethod(nameof(AngryMonkey.CloudLogin.API.CloudLoginController.LoginResult))!;

        Assert.DoesNotContain(action.GetParameters(), parameter =>
            string.Equals(parameter.Name, "currentUser", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CurrentUser_NeverReturnsCredentialMaterial()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        AngryMonkey.CloudLogin.API.CloudLoginController controller = new(fixture.Server)
        {
            ControllerContext = new ControllerContext { HttpContext = fixture.HttpContext }
        };

        ActionResult<CloudUser?> result = await controller.CurrentUser();
        CloudUser response = Assert.IsType<CloudUser>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.All(response.Inputs.SelectMany(input => input.Providers), provider =>
        {
            Assert.Null(provider.PasswordHash);
            Assert.Null(provider.Identifier);
        });
    }

    [Fact]
    public async Task TestSignIn_ValidTestUser_ReturnsOk()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        CloudUser user = await fixture.AddPasswordUserAsync(isTest: true);
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
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.CompleteLogin("https://attacker.example/callback");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLogin_NoAllowlistConfigured_RefusesTheDestination()
    {
        // Fail closed: no configured origins means nothing is approved, not that everything is.
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        LoginController controller = CreateLoginController(fixture);

        IActionResult result = await controller.CompleteLogin("https://anywebsite.example/callback");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, fixture.Store.CreateRequestCount);
    }

    [Fact]
    public async Task CompleteLogin_ApprovedDestination_ReturnsOneTimeRedirect()
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);
        CloudUser user = await fixture.AddPasswordUserAsync();
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
        CloudUser user = await fixture.AddPasswordUserAsync();
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
        CloudUser user = await fixture.AddPasswordUserAsync();
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
        CloudUser user = await fixture.AddPasswordUserAsync();
        string originalHash = user.Inputs[0].Providers.Single().PasswordHash!;
        Guid requestId = Guid.NewGuid();
        fixture.Store.Requests[requestId] = user.ID;
        RequestController controller = CreateRequestController(fixture);

        IActionResult result = await controller.GetUserByRequestId(requestId);

        CloudUser response = Assert.IsType<CloudUser>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(response.Inputs[0].Providers.Single().PasswordHash);
        Assert.Equal(originalHash, user.Inputs[0].Providers.Single().PasswordHash);
    }

    [Fact]
    public void RequestExchange_RequiresServiceAuthentication()
    {
        MethodInfo action = typeof(RequestController)
            .GetMethod(nameof(RequestController.GetUserByRequestId))!;
        AuthorizeAttribute authorization = Assert.Single(
            action.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            ServiceKeyAuthenticationDefaults.AuthenticationScheme,
            authorization.AuthenticationSchemes);
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
