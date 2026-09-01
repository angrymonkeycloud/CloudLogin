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
/// The whole controller exists so a trusted backend caller (CDM) can read and write CloudLogin
/// data without ever holding an end-user session - authenticated by the ServiceKey scheme, not by
/// being a CloudLogin global admin. That distinction is what these tests check for the write
/// endpoints (the whitelist: nothing here should let a caller write a field its own UI would never
/// let a user touch directly - identifiers, lock state, ownership) and for the lookups added
/// alongside them (GetUsersByDisplayName, GetUserByEmail): each answers with no session at all,
/// which is the point - the interactive equivalents either require the caller to already be a
/// global admin or are the deliberately-anonymous discovery endpoint, and neither fits a backend
/// with its own separate notion of "admin".
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

    /// <summary>
    /// This is the lookup a trusted backend uses to resolve "who is this" for its own
    /// access-grant UI - the whole point of it existing here rather than pointing the caller at
    /// the interactive route, which requires the caller to already be a CloudLogin global admin.
    /// A backend with its own, separate notion of admin would 403 there even though it holds a
    /// perfectly valid service credential.
    /// </summary>
    [Fact]
    public async Task GetUsersByDisplayName_finds_a_match_with_no_end_user_session_at_all()
    {
        InMemoryCloudLoginStore users = new();
        CloudUser user = LoginTestFixture.CreateUser();
        users.Users[user.ID] = user;
        ServiceController controller = CreateController(cloudLoginStore: users);

        ActionResult<List<CloudUser>> result = await controller.GetUsersByDisplayName(user.DisplayName!);

        List<CloudUser> found = Assert.IsType<List<CloudUser>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(user.ID, Assert.Single(found).ID);
    }

    [Fact]
    public async Task GetUsersByDisplayName_returnsEmpty_forNoMatch()
    {
        ServiceController controller = CreateController(cloudLoginStore: new InMemoryCloudLoginStore());

        ActionResult<List<CloudUser>> result = await controller.GetUsersByDisplayName("Nobody Here");

        Assert.Empty(Assert.IsType<List<CloudUser>>(Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    public async Task GetUserByEmail_returns_the_matching_user()
    {
        InMemoryCloudLoginStore users = new();
        CloudUser user = LoginTestFixture.CreateUser(email: "dana@example.com");
        users.Users[user.ID] = user;
        ServiceController controller = CreateController(cloudLoginStore: users);

        ActionResult<CloudUser> result = await controller.GetUserByEmail("dana@example.com");

        Assert.Equal(user.ID, Assert.IsType<CloudUser>(Assert.IsType<OkObjectResult>(result.Result).Value).ID);
    }

    [Fact]
    public async Task GetUserByEmail_returns_NotFound_for_an_unknown_address()
    {
        ServiceController controller = CreateController(cloudLoginStore: new InMemoryCloudLoginStore());

        ActionResult<CloudUser> result = await controller.GetUserByEmail("nobody@example.com");

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
