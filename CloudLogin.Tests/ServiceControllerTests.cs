using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.API.Controllers;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// The service-to-service write endpoints exist so a trusted backend caller (CDM's Synchronized
/// field sync) can push an edit made in its own UI back to CloudLogin without ever holding an
/// end-user session - which is exactly what makes the whitelist here the important thing to test:
/// nothing in these endpoints should let a caller write a field its own UI would never let a user
/// touch directly (identifiers, lock state, ownership).
/// </summary>
public class ServiceControllerTests
{
    [Fact]
    public async Task UpdateWorkspace_updates_the_whitelisted_fields_and_returns_the_updated_workspace()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry registry = new(store);
        CloudWorkspace workspace = await registry.CreateAsync("Original Name", Guid.NewGuid());
        ServiceController controller = CreateController(workspaceRegistry: registry);

        ActionResult<CloudWorkspace> result = await controller.UpdateWorkspace(workspace.ID, Values(new
        {
            Name = "New Name",
            BillingContactName = "Dana Haddad",
            BillingEmail = "billing@acme.test"
        }));

        CloudWorkspace updated = Assert.IsType<CloudWorkspace>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("New Name", updated.Name);
        Assert.Equal("Dana Haddad", updated.BillingContactName);
        Assert.Equal("billing@acme.test", updated.BillingEmail);
    }

    [Fact]
    public async Task UpdateWorkspace_rejects_a_field_outside_the_whitelist()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry registry = new(store);
        CloudWorkspace workspace = await registry.CreateAsync("Original Name", Guid.NewGuid());
        ServiceController controller = CreateController(workspaceRegistry: registry);

        ActionResult<CloudWorkspace> result = await controller.UpdateWorkspace(workspace.ID, Values(new
        {
            OwnerUserId = Guid.NewGuid()
        }));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Original Name", (await registry.GetAsync(workspace.ID))!.Name);
    }

    [Fact]
    public async Task UpdateWorkspace_returns_NotFound_for_an_unknown_id()
    {
        ServiceController controller = CreateController(workspaceRegistry: new WorkspaceRegistry(new InMemoryCloudLoginAccountStore()));

        ActionResult<CloudWorkspace> result = await controller.UpdateWorkspace(Guid.NewGuid(), Values(new { Name = "Anything" }));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateUser_updates_the_whitelisted_fields_and_returns_the_updated_user()
    {
        InMemoryCloudLoginStore users = new();
        CloudUser user = LoginTestFixture.CreateUser();
        users.Users[user.ID] = user;
        ServiceController controller = CreateController(cloudLoginStore: users);

        ActionResult<CloudUser> result = await controller.UpdateUser(user.ID, Values(new
        {
            FirstName = "Karim",
            LastName = "Nasr",
            DisplayName = "Karim Nasr",
            Country = "LB",
            Locale = "ar-LB"
        }));

        CloudUser updated = Assert.IsType<CloudUser>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Karim", updated.FirstName);
        Assert.Equal("Nasr", updated.LastName);
        Assert.Equal("Karim Nasr", updated.DisplayName);
        Assert.Equal("LB", updated.Country);
        Assert.Equal("ar-LB", updated.Locale);
    }

    /// <summary>
    /// Lock state is server-managed everywhere else in CloudLogin - a dedicated admin-only endpoint
    /// - so a backend field-sync caller does not get a back door to it here.
    /// </summary>
    [Fact]
    public async Task UpdateUser_rejects_lock_state()
    {
        InMemoryCloudLoginStore users = new();
        CloudUser user = LoginTestFixture.CreateUser();
        users.Users[user.ID] = user;
        ServiceController controller = CreateController(cloudLoginStore: users);

        ActionResult<CloudUser> result = await controller.UpdateUser(user.ID, Values(new { IsLocked = true }));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(users.Users[user.ID].IsLocked);
    }

    /// <summary>
    /// Identifiers (email/phone) are deliberately excluded too - see UserController.Update's own
    /// comment on the same restriction - because they are how a person signs in, not a profile
    /// detail a backend field sync should be able to move.
    /// </summary>
    [Fact]
    public async Task UpdateUser_rejects_a_field_outside_the_whitelist()
    {
        InMemoryCloudLoginStore users = new();
        CloudUser user = LoginTestFixture.CreateUser();
        users.Users[user.ID] = user;
        ServiceController controller = CreateController(cloudLoginStore: users);

        ActionResult<CloudUser> result = await controller.UpdateUser(user.ID, Values(new { Username = "hijacked" }));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateUser_returns_NotFound_for_an_unknown_id()
    {
        ServiceController controller = CreateController(cloudLoginStore: new InMemoryCloudLoginStore());

        ActionResult<CloudUser> result = await controller.UpdateUser(Guid.NewGuid(), Values(new { FirstName = "Anyone" }));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static Dictionary<string, JsonElement> Values(object fields) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(fields))!;

    private static ServiceController CreateController(
        ICloudLoginStore? cloudLoginStore = null,
        ICloudLoginWorkspaceRegistry? workspaceRegistry = null)
    {
        CloudLoginWebConfiguration configuration = new()
        {
            BaseAddress = "https://login.example:443",
            LoginDuration = TimeSpan.FromDays(14),
            WebConfig = static _ => { }
        };

        DefaultHttpContext httpContext = new();
        HttpContextAccessor accessor = new() { HttpContext = httpContext };

        CloudLoginServer server = new(
            new CloudGeographyClient(),
            configuration,
            accessor,
            cloudLoginStore: cloudLoginStore,
            workspaceRegistry: workspaceRegistry);

        return new ServiceController(configuration, server)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }
}
