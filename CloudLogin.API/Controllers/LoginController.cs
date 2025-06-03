using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using AuthenticationProperties = Microsoft.AspNetCore.Authentication.AuthenticationProperties;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin;

[Route("CloudLogin")]
[ApiController]
public class LoginController(CloudLoginConfiguration configuration, CloudLoginServer server) : CloudLoginBaseController(configuration, server)
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
    public IActionResult Login(string identity, bool keepMeSignedIn, bool sameSite, string actionState, string primaryEmail = "", string? input = null, string? redirectUri = null)
    {
        return _server.Login(identity, keepMeSignedIn, sameSite, actionState, primaryEmail, input, redirectUri);
    }

    [HttpGet("Login/CustomLogin")]
    public async Task<IActionResult> CustomLogin(string userInfo, bool keepMeSignedIn, string redirectUri = "", bool sameSite = false, string actionState = "", string primaryEmail = "")
    {
        User user = JsonSerializer.Deserialize<User>(userInfo, CloudLoginSerialization.Options)!;

        return await _server.CustomLogin(user, keepMeSignedIn, redirectUri, sameSite, actionState, primaryEmail);
    }

    [HttpPost("Login/PasswordSignIn")]
    public async Task<IActionResult> PasswordSignIn([FromForm] string email, [FromForm] string password, [FromForm] bool keepMeSignedIn = false, [FromForm] string? redirectUri = null, [FromForm] bool sameSite = false)
    {
        User? user = await _server.ValidateEmailPassword(email, password);

        if (user is null)
            return BadRequest("Invalid email or password.");

        return await _server.CustomLogin(user, keepMeSignedIn, redirectUri ?? "", sameSite);
    }

    [HttpPost("Login/PasswordRegistration")]
    public async Task<IActionResult> PasswordRegistration([FromForm] string email, [FromForm] string password, [FromForm] string firstName, [FromForm] string lastName)
    {
        User user = await _server.PasswordRegistration(email, password, firstName, lastName);

        if (user is null)
            return BadRequest("Invalid email or password.");

        await _server.CustomLogin(user, false, string.Empty, true);

        return Ok(user);
    }

    [HttpGet("Result")]
    public async Task<IActionResult> LoginResult(bool keepMeSignedIn, bool sameSite, string? redirectUri = null, string actionState = "", string primaryEmail = "")
    {
        return await _server.LoginResult(keepMeSignedIn, sameSite, redirectUri, actionState, primaryEmail);
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