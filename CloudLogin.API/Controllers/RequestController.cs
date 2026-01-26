using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin/Request")]
[ApiController]
public class RequestController(CloudLoginWebConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
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
            UserModel? user = await _server.GetUserByRequestId(requestId);

            if (user != null && !string.IsNullOrWhiteSpace(user.ProfilePicture))
                user.ProfilePicture = MakeAbsolute(user.ProfilePicture);

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    private string MakeAbsolute(string value)
    {
        // Already absolute
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return value;

        // Prefer Azure Storage public base URL from configuration; fallback to current request base
        string baseUrl = Configuration.AzureStorage?.PublicBaseUrl?.TrimEnd('/')!;

        string path = value.TrimStart('/');
        return new Uri(new Uri(baseUrl + "/"), path).ToString();
    }
}
