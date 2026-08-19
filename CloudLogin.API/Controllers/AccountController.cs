using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
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
        try
        {
            return Ok(await _server.GetMyOrganizations());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Subscriptions")]
    [Authorize]
    public async Task<ActionResult<List<AccountSubscription>>> Subscriptions()
    {
        try
        {
            return Ok(await _server.GetMySubscriptions());
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
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        try
        {
            return Ok(await _server.CreateOrganization(request.Name));
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
        if (string.IsNullOrWhiteSpace(request.Recipient))
            return BadRequest("Recipient is required.");

        try
        {
            return Ok(await _server.InviteToOrganization(organizationId, request.Recipient, request.Roles));
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

    [HttpPost("BillingProfile/PaymentMethods")]
    [Authorize]
    public async Task<ActionResult<AccountBillingProfile>> AddPaymentMethod([FromBody] AddPaymentMethodRequest request)
    {
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
        catch
        {
            return Problem();
        }
    }
}
