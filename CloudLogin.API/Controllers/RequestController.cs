using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin/Request")]
[ApiController]
public class RequestController(CloudLoginConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpPost("CreateRequest")]
    public async Task<IActionResult> CreateRequest(Guid userId, Guid? requestId = null)
    {
        try
        {
            Guid request = await _server.CreateLoginRequest(userId, requestId);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetUserByRequestId")]
    public async Task<IActionResult> GetUserByRequestId(Guid requestId)
    {
        try
        {
            User? User = await _server.GetUserByRequestId(requestId);

            return Ok(User);
        }
        catch
        {
            return Problem();
        }
    }
}
