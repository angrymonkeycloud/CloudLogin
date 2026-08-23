using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin/Account")]
[ApiController]
public class AccountController(CloudLoginWebConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpGet("Workspaces")]
    [Authorize]
    public async Task<ActionResult<List<CloudWorkspace>>> Workspaces()
    {
        if (Configuration.Workspace is null)
            return NotFound();

        try
        {
            return Ok(await _server.GetMyWorkspaces());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Workspaces/Quota")]
    [Authorize]
    public async Task<ActionResult<CloudWorkspaceQuota>> CloudWorkspaceQuota()
    {
        if (Configuration.Workspace is null)
            return NotFound();

        try
        {
            return Ok(await _server.GetMyWorkspaceQuota());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Workspaces/{workspaceId:guid}/Detail")]
    [Authorize]
    public async Task<ActionResult<CloudWorkspaceDetail>> CloudWorkspaceDetail(Guid workspaceId)
    {
        if (Configuration.Workspace is null)
            return NotFound();

        try
        {
            CloudWorkspaceDetail? workspace = await _server.GetWorkspaceDetail(workspaceId);

            // A non-member gets the same answer as a missing workspace: an identifier alone
            // must not reveal that someone else's workspace exists.
            if (workspace is null)
                return NotFound();

            return Ok(workspace);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Subscriptions")]
    [Authorize]
    public async Task<ActionResult<List<CloudSubscription>>> Subscriptions([FromQuery] bool includeInactive = false)
    {
        if (Configuration.Subscription is null)
            return NotFound();

        try
        {
            return Ok(await _server.GetMySubscriptions(includeInactive));
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("BillingProfile")]
    [Authorize]
    public async Task<ActionResult<CloudBillingProfile?>> BillingProfile()
    {
        if (Configuration.Payment is null)
            return NotFound();

        try
        {
            return Ok(await _server.GetMyBillingProfile());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("Workspaces")]
    [Authorize]
    public async Task<ActionResult<CloudWorkspace>> CreateWorkspace([FromBody] CloudLoginCreateWorkspaceRequest request)
    {
        if (Configuration.Workspace is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        try
        {
            return Ok(await _server.CreateWorkspace(request.Name));
        }
        catch (CloudWorkspaceLimitReachedException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("Workspaces/{workspaceId:guid}/Invite")]
    [Authorize]
    public async Task<ActionResult<CloudWorkspaceInvitation>> InviteToWorkspace(Guid workspaceId, [FromBody] CloudLoginInviteToWorkspaceRequest request)
    {
        if (Configuration.Workspace is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Recipient))
            return BadRequest("Recipient is required.");

        try
        {
            return Ok(await _server.InviteToWorkspace(workspaceId, request.Recipient, request.Roles));
        }
        catch (CloudWorkspaceLimitReachedException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPut("Workspaces/{workspaceId:guid}")]
    [Authorize]
    public async Task<ActionResult<CloudWorkspace>> UpdateWorkspace(Guid workspaceId, [FromBody] CloudWorkspace workspace)
    {
        if (Configuration.Workspace is null)
            return NotFound();

        if (workspaceId != workspace.Id)
            return BadRequest("Route id and body id must match.");

        try
        {
            return Ok(await _server.UpdateWorkspace(workspace));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpDelete("Workspaces/{workspaceId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteWorkspace(Guid workspaceId)
    {
        if (Configuration.Workspace is null)
            return NotFound();

        try
        {
            await _server.DeleteWorkspace(workspaceId);
            return NoContent();
        }
        catch (CloudWorkspaceDeletionBlockedException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (NotSupportedException)
        {
            // The host's account store predates deletion support.
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpDelete("Subscriptions/{subscriptionId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteSubscription(Guid subscriptionId)
    {
        if (Configuration.Subscription is null)
            return NotFound();

        try
        {
            await _server.DeleteSubscription(subscriptionId);
            return NoContent();
        }
        catch (CloudSubscriptionDeletionBlockedException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (NotSupportedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("BillingProfile/PaymentMethods")]
    [Authorize]
    public async Task<ActionResult<CloudBillingProfile>> AddPaymentMethod([FromBody] CloudLoginAddPaymentMethodRequest request)
    {
        if (Configuration.Payment is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Method.Provider) || string.IsNullOrWhiteSpace(request.Method.Reference))
            return BadRequest("Provider and Reference are required.");

        try
        {
            return Ok(await _server.AddPaymentMethod(request.Method, request.WorkspaceId));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("BillingProfile/PaymentMethods/Remove")]
    [Authorize]
    public async Task<ActionResult<CloudBillingProfile>> RemovePaymentMethod([FromBody] CloudLoginRemovePaymentMethodRequest request)
    {
        if (Configuration.Payment is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Reference))
            return BadRequest("Provider and Reference are required.");

        try
        {
            return Ok(await _server.RemovePaymentMethod(request.Provider, request.Reference, request.WorkspaceId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch
        {
            return Problem();
        }
    }
}
