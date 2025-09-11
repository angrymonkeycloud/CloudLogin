using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin")]
[ApiController]
public class LoginController(CloudLoginConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    //[HttpGet("GetClient")]
    //public ActionResult<CloudLoginClient> GetClient(string serverLoginUrl) => new CloudLoginClient()
    //{
    //    HttpServer = new() { BaseAddress = new(serverLoginUrl) },
    //    Providers = Configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.HandleUpdateOnly, key.Label)
    //    {
    //        IsCodeVerification = key.IsCodeVerification,
    //        HandlesPhoneNumber = key.HandlesPhoneNumber,
    //        HandlesEmailAddress = key.HandlesEmailAddress,
    //        HandleUpdateOnly = key.HandleUpdateOnly,
    //        InputRequired = key.InputRequired,
    //    }).ToList(),

    //    FooterLinks = Configuration.FooterLinks,
    //    RedirectUri = Configuration.RedirectUri
    //};

    [HttpGet("Login/{identity}")]
    public IActionResult Login(string identity, bool keepMeSignedIn, bool sameSite, string primaryEmail = "", string? input = null, string? redirectUri = null)
    {
        return _server.Login(identity, keepMeSignedIn, sameSite, primaryEmail, input, redirectUri);
    }

    [HttpGet("Login/CustomLogin")]
    public async Task<IActionResult> CustomLogin(string userInfo, bool keepMeSignedIn, string redirectUri = "", bool sameSite = false, string primaryEmail = "")
    {
        User user = JsonSerializer.Deserialize<User>(userInfo, CloudLoginSerialization.Options)!;

        return await _server.CustomLogin(user, keepMeSignedIn, redirectUri, sameSite, primaryEmail);
    }

    [HttpPost("Login/PasswordSignIn")]
    public async Task<IActionResult> PasswordSignIn([FromForm] string email, [FromForm] string password, [FromForm] bool keepMeSignedIn = false, [FromForm] string? redirectUri = null, [FromForm] bool sameSite = false)
    {
        PasswordLoginRequest request = PasswordLoginRequest.Create(email, password, keepMeSignedIn);
        bool result = await _server.PasswordLogin(request);

        if (!result)
            return BadRequest("Invalid email or password.");

        return Ok();
    }

    [HttpPost("Login/PasswordRegistration")]
    public async Task<IActionResult> PasswordRegistration([FromForm] string input, [FromForm] string inputFormat, [FromForm] string password, [FromForm] string firstName, [FromForm] string lastName, [FromForm] string displayName)
    {
        if (!Enum.TryParse<InputFormat>(inputFormat, true, out InputFormat format))
            return BadRequest("Invalid input format.");

        PasswordRegistrationRequest request = PasswordRegistrationRequest.Create(input, format, password, firstName, lastName, displayName);
        User user = await _server.PasswordRegistration(request);

        if (user is null)
            return BadRequest("Registration failed.");

        return Ok(user);
    }

    [HttpPost("Login/CodeRegistration")]
    public async Task<IActionResult> CodeRegistration([FromForm] string input, [FromForm] string inputFormat, [FromForm] string firstName, [FromForm] string lastName, [FromForm] string displayName)
    {
        if (!Enum.TryParse<InputFormat>(inputFormat, true, out InputFormat format))
            return BadRequest("Invalid input format.");

        CodeRegistrationRequest request = CodeRegistrationRequest.Create(input, format, firstName, lastName, displayName);
        User user = await _server.CodeRegistration(request);

        return Ok(user);
    }

    [HttpGet("Result")]
    public async Task<IActionResult> LoginResult(bool keepMeSignedIn, bool sameSite, string? redirectUri = null, string primaryEmail = "")
    {
        return await _server.LoginResult(keepMeSignedIn, sameSite, redirectUri, primaryEmail);
    }

    [HttpGet("Update")]
    public async Task<IActionResult> Update(string redirectUri, string? userInfo)
    {
        return await _server.UpdateAuth(redirectUri, userInfo);
    }

    [HttpGet("Logout")]
    public async Task<IActionResult> Logout(string? redirectUri)
    {
        return await _server.Logout(redirectUri);
    }
}