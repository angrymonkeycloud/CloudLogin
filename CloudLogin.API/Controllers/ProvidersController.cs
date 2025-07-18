using Microsoft.AspNetCore.Mvc;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("api/Providers")]
[ApiController]
public class ProvidersController(CloudLoginConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpGet("Test")]
    public async Task<ActionResult> Test()
    {
       return Ok("Test");
    }

    [HttpGet("")]
    public async Task<ActionResult> GetProviders()
    {
        try
        {
            List<ProviderDefinition> providers = await _server.GetProviders();

            return Ok(providers);
        }
        catch (Exception e)
        {
            return Problem(e.ToString());
        }
    }
}
