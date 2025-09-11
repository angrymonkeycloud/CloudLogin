using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("Request/{key}")]
[ApiController]
public class AppController(CloudLoginConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpGet]
    public IActionResult Get(string key)
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        return Redirect($"{baseUrl}/?redirectUri={key}");
    }
}
