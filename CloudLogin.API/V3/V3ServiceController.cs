using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.V3;

/// <summary>
/// V3 service-to-service reads for trusted backends, authenticated by the ServiceKey scheme
/// only — never a browser cookie. Returns the minimal <see cref="V3ServiceUserResponse"/> view.
/// </summary>
[Route("api/v3/service")]
[Authorize(AuthenticationSchemes = ServiceKeyAuthenticationDefaults.AuthenticationScheme)]
public sealed class V3ServiceController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    [HttpGet("users/{userId:guid}")]
    public async Task<ActionResult<V3ServiceUserResponse>> GetUser(Guid userId)
    {
        CloudUser? user = await Server.GetUserById(userId);
        return user is null ? NotFound() : Ok(ToServiceView(user));
    }
}
