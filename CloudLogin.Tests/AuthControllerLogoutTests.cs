using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryMonkey.CloudLogin.Tests;

public class AuthControllerLogoutTests
{
    [Fact]
    public async Task Logout_ClearsConsumerCookie_AndFallsBackToTheLocalPageWhenNoAuthorityTransactionCanBeOpened()
    {
        RecordingAuthenticationService authentication = new();
        AuthController controller = CreateController(authentication);

        IActionResult result = await controller.Logout("/signed-out?from=menu");

        Assert.Equal(1, authentication.SignOutCount);
        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/signed-out?from=menu", redirect.Url);
    }

    [Theory]
    [InlineData("https://attacker.example/callback")]
    [InlineData("//attacker.example/callback")]
    [InlineData("/\attacker.example")]
    public async Task Logout_NormalizesExternalConsumerReturnUrl(string returnUrl)
    {
        RecordingAuthenticationService authentication = new();
        AuthController controller = CreateController(authentication);

        IActionResult result = await controller.Logout(returnUrl);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
        Assert.Equal(1, authentication.SignOutCount);
    }

    private static AuthController CreateController(RecordingAuthenticationService authentication)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LoginUrl"] = "https://login.example"
            })
            .Build();

        DefaultHttpContext httpContext = new();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("app.example");
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationService>(authentication)
            .BuildServiceProvider();

        ActionContext actionContext = new(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor());

        AuthController controller = new(
            configuration,
            new CloudLoginServerConfiguration(),
            new UnusedHttpClientFactory(),
            NullLogger<AuthController>.Instance,
            new EphemeralDataProtectionProvider())
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new UrlHelper(actionContext)
        };

        return controller;
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
