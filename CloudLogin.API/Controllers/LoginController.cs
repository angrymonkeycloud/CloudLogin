using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin")]
[ApiController]
public class LoginController(CloudLoginWebConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpGet("Login/{identity}")]
    public async Task<IActionResult> Login(string identity, bool keepMeSignedIn, bool sameSite, string primaryEmail = "", string? input = null, string? referer = null, bool isMobileApp = false)
        => await _server.Login(identity, keepMeSignedIn, sameSite, primaryEmail, input, referer, isMobileApp);

    [HttpGet("Login/CustomLogin")]
    public async Task<IActionResult> CustomLogin(Guid userId, bool keepMeSignedIn, string? referer = null, bool sameSite = false, bool isMobileApp = false)
        => await _server.CustomLogin(userId, keepMeSignedIn, referer, sameSite, isMobileApp);

    [HttpPost("Login/TestSignIn")]
    public async Task<IActionResult> TestSignIn([FromForm] Guid userId, [FromForm] bool keepMeSignedIn = false)
        => await _server.TestLogin(userId, keepMeSignedIn) ? Ok() : Unauthorized();

    [HttpGet("Login/Complete")]
    public async Task<IActionResult> CompleteLogin(string? referer = null, bool isMobileApp = false)
    {
        try
        {
            return Ok(await _server.CompleteLoginRedirect(referer, isMobileApp));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("Login/PasswordSignIn")]
    public async Task<IActionResult> PasswordSignIn([FromForm] string email, [FromForm] string password, [FromForm] bool keepMeSignedIn = false, [FromForm] string? referer = null)
    {
        PasswordLoginRequest request = PasswordLoginRequest.Create(email, password, keepMeSignedIn);
        bool result = await _server.PasswordLogin(request);

        if (!result)
            return BadRequest("Invalid email or password.");

        if (!string.IsNullOrWhiteSpace(referer))
        {
            try
            {
                return Redirect(await _server.CompleteLoginRedirect(referer));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        return Ok();
    }

    [HttpPost("Login/PasswordRegistration")]
    public async Task<IActionResult> PasswordRegistration([FromForm] string input, [FromForm] string inputFormat, [FromForm] string? password, [FromForm] string firstName, [FromForm] string lastName, [FromForm] string displayName, [FromForm] string? referer = null)
    {
        if (!Enum.TryParse<InputFormat>(inputFormat, true, out InputFormat format))
            return BadRequest("Invalid input format.");

        PasswordRegistrationRequest request = PasswordRegistrationRequest.Create(input, format, password, firstName, lastName, displayName);
        UserModel user = await _server.PasswordRegistration(request);

        if (user is null)
            return BadRequest("Registration failed.");

        return Ok(user);
    }

    [HttpPost("Login/CodeRegistration")]
    public async Task<IActionResult> CodeRegistration([FromForm] string input, [FromForm] string inputFormat, [FromForm] string firstName, [FromForm] string lastName, [FromForm] string displayName, [FromForm] string? referer = null)
    {
        if (!Enum.TryParse(inputFormat, true, out InputFormat format))
            return BadRequest("Invalid input format.");

        CodeRegistrationRequest request = CodeRegistrationRequest.Create(input, format, firstName, lastName, displayName);
        UserModel user = await _server.CodeRegistration(request);

        return Ok(user);
    }

    /// <summary>
    /// OAuth callback endpoint - this is where OAuth providers redirect back to
    /// This should be configured in your OAuth provider settings (Google, Microsoft, etc.)
    /// Note: This uses redirectUri for OAuth provider compatibility only
    /// </summary>
    [HttpGet("Result")]
    public async Task<IActionResult> OAuthResult(bool keepMeSignedIn = false, bool sameSite = false, bool isMobileApp = false)
        => await _server.LoginResult(keepMeSignedIn, sameSite, isMobileApp);

    /// <summary>
    /// Legacy endpoint for backward compatibility
    /// </summary>
    [HttpGet("LoginResult")]
    public async Task<IActionResult> LoginResult(bool keepMeSignedIn = false, bool sameSite = false, bool isMobileApp = false)
        => await OAuthResult(keepMeSignedIn, sameSite, isMobileApp);

    [HttpGet("Update")]
    public async Task<IActionResult> Update(string referer, string? userInfo, bool isMobileApp = false)
        => await _server.UpdateAuth(referer, userInfo, isMobileApp);

    [HttpGet("Logout")]
    public async Task<IActionResult> Logout(string? referer, bool isMobileApp = false)
        => await _server.Logout(referer, isMobileApp);

    // URL generation endpoints
    [HttpGet("GetLoginUrl")]
    public ActionResult<string> GetLoginUrl(string? referer = null, bool isMobileApp = false)
        => Ok(_server.GetLoginUrl(referer, isMobileApp));

    [HttpGet("GetProviderLoginUrl")]
    public ActionResult<string> GetProviderLoginUrl(string providerCode, string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false)
    {
        try
        {
            return Ok(_server.GetProviderLoginUrl(providerCode, referer, isMobileApp, keepMeSignedIn));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("GetCustomLoginUrl")]
    public ActionResult<string> GetCustomLoginUrl(string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false, string? userHint = null)
        => Ok(_server.GetCustomLoginUrl(referer, isMobileApp, keepMeSignedIn, userHint));
}
