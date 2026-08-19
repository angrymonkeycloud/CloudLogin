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
    [HttpGet("Organizations")]
    public async Task<ActionResult<List<CloudLoginOrganization>>> GetAllOrganizations()
    {
        try
        {
            return Ok(await _server.GetAllOrganizations());
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Organizations/{organizationId:guid}")]
    public async Task<ActionResult<CloudLoginOrganization>> GetOrganization(Guid organizationId)
    {
        try
        {
            CloudLoginOrganization? organization = await _server.GetOrganizationById(organizationId);

            if (organization == null)
                return NotFound();

            return Ok(organization);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Organizations/{organizationId:guid}/Members")]
    public async Task<ActionResult<List<CloudLoginOrganizationMember>>> GetOrganizationMembers(Guid organizationId)
    {
        try
        {
            return Ok(await _server.GetOrganizationMembers(organizationId));
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("Users")]
    public async Task<ActionResult<List<UserModel>>> GetAllUsers()
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
    public async Task<ActionResult<UserModel>> GetUser(Guid userId)
    {
        try
        {
            UserModel? user = await _server.GetUserById(userId);

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
    public async Task<ActionResult<List<AccountSubscription>>> GetAllSubscriptions()
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
    public async Task<ActionResult<AccountSubscription>> GetSubscription(Guid subscriptionId)
    {
        try
        {
            AccountSubscription? subscription = await _server.GetSubscriptionById(subscriptionId);

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
