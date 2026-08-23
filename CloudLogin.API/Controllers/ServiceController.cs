using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

/// <summary>
/// Server-to-server lookups for trusted backend callers (e.g. AngryMonkey.Portal), gated by
/// the "ServiceKey" scheme only — never reachable via a browser cookie session.
/// </summary>
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

    [HttpGet("Subscriptions")]
    public async Task<ActionResult<List<CloudSubscription>>> GetAllSubscriptions()
    {
        try
        {
            return Ok(await _server.GetAllSubscriptions());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Subscriptions/{subscriptionId:guid}")]
    public async Task<ActionResult<CloudSubscription>> GetSubscription(Guid subscriptionId)
    {
        try
        {
            CloudSubscription? subscription = await _server.GetSubscriptionById(subscriptionId);

            if (subscription == null)
                return NotFound();

            return Ok(subscription);
        }
        catch
        {
            return Problem();
        }
    }
}
