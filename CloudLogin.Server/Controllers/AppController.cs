using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;
[Route("Request/{key}")]
[ApiController]
public class AppController(CloudLoginConfiguration configuration, CosmosMethods cosmosMethods) : BaseController(configuration, cosmosMethods)
{
    [HttpGet]
    public IActionResult Get(string key)
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        return Redirect($"{baseUrl}/?actionState=mobile&redirectUri={key}");
    }
}
