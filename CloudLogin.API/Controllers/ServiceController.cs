using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

/// <summary>
/// Server-to-server lookups for trusted backend callers (e.g. AngryMonkey.Portal), gated by
/// the "ServiceKey" scheme only — never reachable via a browser cookie session.
/// </summary>
/// <remarks>
/// Deliberately not version-gated. This is the authority's own trusted backend channel, like its
/// UI and flow endpoints, rather than one of the versioned public façades — and
/// <c>api/v3/service</c> covers only <c>users/{id}</c>, so gating this to V2 removed workspace and
/// membership reads that have no replacement. With the API version defaulting to V3 the gate made
/// every <c>CloudLogin/Service/*</c> route answer 404 to a correctly authenticated caller, which
/// reads to the calling component as its data simply not being there.
/// </remarks>
[Route("CloudLogin/Service")]
[ApiController]
[Authorize(AuthenticationSchemes = ServiceKeyAuthenticationDefaults.AuthenticationScheme)]
public class ServiceController(CloudLoginWebConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpGet("Workspaces")]
    public async Task<ActionResult<List<CloudWorkspace>>> GetAllWorkspaces()
    {
        try
        {
            return Ok(await _server.GetAllWorkspaces());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Workspaces/{workspaceId:guid}")]
    public async Task<ActionResult<CloudWorkspace>> GetWorkspace(Guid workspaceId)
    {
        try
        {
            CloudWorkspace? workspace = await _server.GetWorkspaceById(workspaceId);

            if (workspace == null)
                return NotFound();

            return Ok(workspace);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Workspaces/{workspaceId:guid}/Members")]
    public async Task<ActionResult<List<CloudWorkspaceMember>>> GetWorkspaceMembers(Guid workspaceId)
    {
        try
        {
            return Ok(await _server.GetWorkspaceMembers(workspaceId));
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users")]
    public async Task<ActionResult<List<CloudUser>>> GetAllUsers()
    {
        try
        {
            return Ok(await _server.GetAllUsers());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users/{userId:guid}")]
    public async Task<ActionResult<CloudUser>> GetUser(Guid userId)
    {
        try
        {
            CloudUser? user = await _server.GetUserById(userId);

            if (user == null)
                return NotFound();

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// The two lookups a trusted backend needs to resolve a candidate for its own access-grant UI
    /// (someone typed a name or an email; which account is that?) without holding an end-user
    /// session. The interactive equivalents exist too, but one requires the caller to already be a
    /// CloudLogin global admin and the other is deliberately public/anonymous-safe — neither fits a
    /// backend that has its own, separate notion of "admin" and wants the full profile back.
    /// </summary>
    [HttpGet("Users/ByDisplayName")]
    public async Task<ActionResult<List<CloudUser>>> GetUsersByDisplayName(string displayName)
    {
        try
        {
            return Ok(await _server.GetUsersByDisplayName(displayName));
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users/ByEmail")]
    public async Task<ActionResult<CloudUser>> GetUserByEmail(string email)
    {
        try
        {
            CloudUser? user = await _server.GetUserByEmailAddress(email);

            if (user == null)
                return NotFound();

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Partial update of a workspace's own fields (name, billing contact) - the fields a trusted
    /// backend caller (CDM's Synchronized field sync) is allowed to write back. Anything outside
    /// this whitelist is rejected rather than silently ignored, so a caller finds out immediately
    /// if it is asking for something this endpoint does not do.
    /// </summary>
    [HttpPut("Workspaces/{workspaceId:guid}")]
    public async Task<ActionResult<CloudWorkspace>> UpdateWorkspace(Guid workspaceId, [FromBody] Dictionary<string, JsonElement> values)
    {
        try
        {
            CloudWorkspace? workspace = await _server.GetWorkspaceById(workspaceId);

            if (workspace == null)
                return NotFound();

            foreach ((string key, JsonElement value) in values)
            {
                switch (key)
                {
                    case nameof(CloudWorkspace.Name): workspace.Name = value.GetString() ?? workspace.Name; break;
                    case nameof(CloudWorkspace.BillingContactName): workspace.BillingContactName = value.GetString(); break;
                    case nameof(CloudWorkspace.BillingEmail): workspace.BillingEmail = value.GetString(); break;
                    default: return BadRequest($"Field '{key}' cannot be updated through the service endpoint.");
                }
            }

            return Ok(await _server.UpdateWorkspaceAsService(workspace));
        }
        catch
        {
            return Problem();
        }
    }

    /// <summary>
    /// Partial update of a user's own profile fields - the same whitelist
    /// <see cref="UserController.Update"/> already applies to an end-user's own edit. Identifiers
    /// (email, phone) and lock state are deliberately excluded here too: they are server-managed,
    /// not something a backend caller should be able to overwrite by way of a CDM field sync.
    /// </summary>
    [HttpPut("Users/{userId:guid}")]
    public async Task<ActionResult<CloudUser>> UpdateUser(Guid userId, [FromBody] Dictionary<string, JsonElement> values)
    {
        try
        {
            CloudUser? user = await _server.GetUserById(userId);

            if (user == null)
                return NotFound();

            foreach ((string key, JsonElement value) in values)
            {
                switch (key)
                {
                    case nameof(CloudUser.FirstName): user.FirstName = value.GetString(); break;
                    case nameof(CloudUser.LastName): user.LastName = value.GetString(); break;
                    case nameof(CloudUser.DisplayName): user.DisplayName = value.GetString(); break;
                    case nameof(CloudUser.Country): user.Country = value.GetString(); break;
                    case nameof(CloudUser.Locale): user.Locale = value.GetString(); break;
                    default: return BadRequest($"Field '{key}' cannot be updated through the service endpoint.");
                }
            }

            await _server.UpdateUser(user);

            return Ok(await _server.GetUserById(userId));
        }
        catch
        {
            return Problem();
        }
    }
}
