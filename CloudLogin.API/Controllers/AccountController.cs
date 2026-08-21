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
    [HttpGet("Organizations")]
    [Authorize]
    public async Task<ActionResult<List<CloudLoginOrganization>>> Organizations()
    {
        if (Configuration.Organization is null)
            return NotFound();

        try
        {
            return Ok(await _server.GetMyOrganizations());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Organizations/Quota")]
    [Authorize]
    public async Task<ActionResult<OrganizationQuota>> OrganizationQuota()
    {
        if (Configuration.Organization is null)
            return NotFound();

        try
        {
            return Ok(await _server.GetMyOrganizationQuota());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Organizations/{organizationId:guid}/Workspace")]
    [Authorize]
    public async Task<ActionResult<OrganizationWorkspace>> OrganizationWorkspace(Guid organizationId)
    {
        if (Configuration.Organization is null)
            return NotFound();

        try
        {
            OrganizationWorkspace? workspace = await _server.GetOrganizationWorkspace(organizationId);

            // A non-member gets the same answer as a missing organization: an identifier alone
            // must not reveal that someone else's organization exists.
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
    public async Task<ActionResult<List<AccountSubscription>>> Subscriptions([FromQuery] bool includeInactive = false)
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
    public async Task<ActionResult<AccountBillingProfile?>> BillingProfile()
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

    [HttpPost("Organizations")]
    [Authorize]
    public async Task<ActionResult<CloudLoginOrganization>> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        if (Configuration.Organization is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        try
        {
            return Ok(await _server.CreateOrganization(request.Name));
        }
        catch (OrganizationLimitReachedException exception)
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

    [HttpPost("Organizations/{organizationId:guid}/Invite")]
    [Authorize]
    public async Task<ActionResult<CloudLoginOrganizationInvitation>> InviteToOrganization(Guid organizationId, [FromBody] InviteToOrganizationRequest request)
    {
        if (Configuration.Organization is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Recipient))
            return BadRequest("Recipient is required.");

        try
        {
            return Ok(await _server.InviteToOrganization(organizationId, request.Recipient, request.Roles));
        }
        catch (OrganizationLimitReachedException exception)
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

    [HttpPut("Organizations/{organizationId:guid}")]
    [Authorize]
    public async Task<ActionResult<CloudLoginOrganization>> UpdateOrganization(Guid organizationId, [FromBody] CloudLoginOrganization organization)
    {
        if (Configuration.Organization is null)
            return NotFound();

        if (organizationId != organization.Id)
            return BadRequest("Route id and body id must match.");

        try
        {
            return Ok(await _server.UpdateOrganization(organization));
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

    [HttpDelete("Organizations/{organizationId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteOrganization(Guid organizationId)
    {
        if (Configuration.Organization is null)
            return NotFound();

        try
        {
            await _server.DeleteOrganization(organizationId);
            return NoContent();
        }
        catch (OrganizationDeletionBlockedException exception)
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
        catch (SubscriptionDeletionBlockedException exception)
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
    public async Task<ActionResult<AccountBillingProfile>> AddPaymentMethod([FromBody] AddPaymentMethodRequest request)
    {
        if (Configuration.Payment is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Method.Provider) || string.IsNullOrWhiteSpace(request.Method.Reference))
            return BadRequest("Provider and Reference are required.");

        try
        {
            return Ok(await _server.AddPaymentMethod(request.Method, request.OrganizationId));
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
    public async Task<ActionResult<AccountBillingProfile>> RemovePaymentMethod([FromBody] RemovePaymentMethodRequest request)
    {
        if (Configuration.Payment is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Reference))
            return BadRequest("Provider and Reference are required.");

        try
        {
            return Ok(await _server.RemovePaymentMethod(request.Provider, request.Reference, request.OrganizationId));
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
