using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;
[Route("Request/{key}")]
[ApiController]
public class AppController : BaseController
{
    [HttpGet]
    public IActionResult Get(string key)
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";
        // your logic here
        return Redirect($"{baseUrl}/?actionState=mobile&redirectUri={key}");
    }
}
