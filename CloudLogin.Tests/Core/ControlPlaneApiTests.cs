using System.Text.Json;
using AngryMonkey.CloudLogin.API.V3.Admin;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The admin HTTP surface: every action is gated by the permission it names, the caller's roles are read from the stored account, and a
/// secret key appears in exactly one response, the one that creates it.
/// </summary>
public class ControlPlaneApiTests
{
    private readonly ControlPlaneFixture _plane = new();

    private sealed record Console(LoginTestFixture Fixture, V3AdminApplicationsController Applications, V3AdminSecretKeysController Keys, V3AdminController Overview);

    private Console SignedIn(params AdminRoles[] roles)
    {
        LoginTestFixture fixture = new();
        CloudUser user = fixture.AddPasswordUserAsync().GetAwaiter().GetResult(); // synchronous on purpose: the accessor holds the context in an AsyncLocal

        _plane.Users.CreateAsync(new UserDocument { Id = user.Id.ToString(), FirstName = "Test", LastName = "Person", AdminRoles = [.. roles] }).GetAwaiter().GetResult();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddControllers(); // supplies the ProblemDetailsFactory the controllers use for refusals
        services.AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationService>(fixture.Authentication);
        services.AddSingleton<IAdminAuthorizer>(new AdminAuthorizer(_plane.Users));
        services.AddSingleton(_plane.Apps);
        services.AddSingleton(_plane.Keys);
        services.AddSingleton<IAuditEventRepository>(_plane.AuditEvents);
        services.AddSingleton(new AdminDashboardService(_plane.Users, _plane.Sessions, _plane.Apps, _plane.Keys, _plane.AuditEvents));
        fixture.HttpContext.RequestServices = services.BuildServiceProvider();
        fixture.Accessor.HttpContext = fixture.HttpContext;
        fixture.AuthenticateAs(user);

        ControllerContext context = new() { HttpContext = fixture.HttpContext };

        return new(
            fixture,
            new V3AdminApplicationsController(fixture.Configuration, fixture.Server) { ControllerContext = context },
            new V3AdminSecretKeysController(fixture.Configuration, fixture.Server) { ControllerContext = context },
            new V3AdminController(fixture.Configuration, fixture.Server) { ControllerContext = context });
    }

    private static int? Status(IActionResult result) => (result as ObjectResult)?.StatusCode ?? (result as StatusCodeResult)?.StatusCode;

    private static V3AdminCreateSecretKeyRequest NewKey(DateTimeOffset? expiresOn = null) => new() { Label = "Partner websites", ExpiresOn = expiresOn };

    [Fact]
    public async Task AnApplicationAdministrator_CanCreateAKey_AndSeesItsSecretOnce()
    {
        Console console = SignedIn(AdminRoles.ApplicationAdministrator);

        OkObjectResult created = Assert.IsType<OkObjectResult>(await console.Keys.Create(NewKey()));
        V3AdminIssuedSecretKeyResponse issued = Assert.IsType<V3AdminIssuedSecretKeyResponse>(created.Value);
        Assert.StartsWith(SecretKeyService.SecretPrefix, issued.KeyValue);
        Assert.Null(issued.Key.ExpiresOn);

        OkObjectResult list = Assert.IsAssignableFrom<OkObjectResult>(await console.Keys.List());
        string json = JsonSerializer.Serialize(list.Value);
        Assert.DoesNotContain(issued.KeyValue, json);
        Assert.Contains(issued.Key.Id, json);
    }

    [Fact]
    public async Task AKey_CanBeCreatedWithAnExpiry()
    {
        Console console = SignedIn(AdminRoles.ApplicationAdministrator);
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(90);

        V3AdminIssuedSecretKeyResponse issued = Assert.IsType<V3AdminIssuedSecretKeyResponse>(Assert.IsType<OkObjectResult>(await console.Keys.Create(NewKey(expiry))).Value);

        Assert.Equal(expiry, issued.Key.ExpiresOn);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(await console.Keys.Create(NewKey(DateTimeOffset.UtcNow.AddDays(-1)))));
    }

    [Fact]
    public async Task AUserAdministrator_CannotTouchApplicationsOrKeys()
    {
        Console console = SignedIn(AdminRoles.UserAdministrator);

        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Keys.Create(NewKey())));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Keys.List()));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Applications.List()));
        Assert.DoesNotContain(_plane.SecretKeyStore.Documents.Values, key => !key.IsDeployment);
    }

    [Fact]
    public async Task AnAuditReader_CanReadTheDashboardAndAudit_ButChangeNothing()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        Console console = SignedIn(AdminRoles.AuditReader);

        Assert.IsType<OkObjectResult>(await console.Overview.Dashboard());
        Assert.IsType<OkObjectResult>(await console.Overview.Audit(null, null, null, null, null, null, null, null));
        Assert.IsAssignableFrom<OkObjectResult>(await console.Applications.List());
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Keys.Create(NewKey())));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Applications.Block("portal", null)));
    }

    [Fact]
    public async Task AnAccountWithNoAdministratorRole_IsRefusedEverywhere_EvenWhenSignedIn()
    {
        Console console = SignedIn();

        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Overview.Me()));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Overview.Dashboard()));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Applications.List()));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Keys.List()));
    }

    [Fact]
    public async Task ASignedOutCaller_GetsUnauthorized()
    {
        Console console = SignedIn(AdminRoles.FullAdministrator);
        console.Fixture.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        Assert.IsType<UnauthorizedResult>(await console.Applications.List());
        Assert.IsType<UnauthorizedResult>(await console.Keys.List());
    }

    [Fact]
    public async Task ARoleRemovedFromTheAccount_StopsWorkingOnTheNextRequest()
    {
        Console console = SignedIn(AdminRoles.ApplicationAdministrator);
        Assert.IsAssignableFrom<OkObjectResult>(await console.Applications.List());

        UserDocument stored = (await _plane.Users.GetAllAsync()).Single();
        stored.AdminRoles = [];
        await _plane.Users.ReplaceAsync(stored);

        Assert.Equal(StatusCodes.Status403Forbidden, Status(await console.Applications.List()));
    }

    [Fact]
    public async Task DomainErrors_BecomeTheRightHttpStatuses()
    {
        Console console = SignedIn(AdminRoles.ApplicationAdministrator);
        V3AdminIssuedSecretKeyResponse issued = Assert.IsType<V3AdminIssuedSecretKeyResponse>(Assert.IsType<OkObjectResult>(await console.Keys.Create(NewKey())).Value);
        await console.Keys.Revoke(issued.Key.Id, null);

        Assert.Equal(StatusCodes.Status409Conflict, Status(await console.Keys.Revoke(issued.Key.Id, null)));
        Assert.Equal(StatusCodes.Status404NotFound, Status(await console.Applications.Get("never-seen")));
        Assert.Equal(StatusCodes.Status404NotFound, Status(await console.Keys.Revoke("missing-key", null)));
        Assert.Equal(StatusCodes.Status400BadRequest, Status(await console.Keys.Create(new V3AdminCreateSecretKeyRequest { Label = new string('x', 61) })));
    }

    [Fact]
    public async Task BlockingAnApplication_EndsItsSessions_AndRefusesIt_AndIsAudited()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        await _plane.SessionService.IssueFamilyAsync(Guid.NewGuid(), "portal", null, null, null);
        Console console = SignedIn(AdminRoles.ApplicationAdministrator);

        OkObjectResult blocked = Assert.IsType<OkObjectResult>(await console.Applications.Block("portal", new V3AdminBlockApplicationRequest { Reason = "compromised" }));

        Assert.Equal(1, Assert.IsType<V3AdminRevokedCountResponse>(blocked.Value).Revoked);
        Assert.Equal(ClientAuthenticationFailures.Blocked, (await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
        Assert.NotNull(Assert.Single(_plane.Events(AuditEventTypes.ApplicationBlocked)).ActorUserId);

        V3AdminApplicationResponse read = Assert.IsType<V3AdminApplicationResponse>(Assert.IsType<OkObjectResult>(await console.Applications.Get("portal")).Value);
        Assert.True(read.IsBlocked);
        Assert.Equal("compromised", read.BlockReason);
    }

    [Fact]
    public async Task RevokingAKey_NeedsTheRevokePermission_NotJustReadAccess()
    {
        IssuedSecretKey issued = await _plane.Keys.CreateAsync(_plane.Admin, "Partner websites", null);

        Console reader = SignedIn(AdminRoles.AuditReader);
        Assert.Equal(StatusCodes.Status403Forbidden, Status(await reader.Keys.Revoke(issued.Key.Id, null)));

        Console administrator = SignedIn(AdminRoles.ApplicationAdministrator);
        Assert.IsType<NoContentResult>(await administrator.Keys.Revoke(issued.Key.Id, new V3AdminRevokeRequest { Reason = "test" }));
        Assert.False((await _plane.AuthenticateAsync("portal", issued.Secret)).Succeeded);
    }
}
