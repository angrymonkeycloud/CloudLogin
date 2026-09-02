using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin")]
[ApiController]
public class LoginController(CloudLoginWebConfiguration configuration, CloudLoginServer server) : CloudLoginBaseController(configuration, server)
{
    private const int MaximumLegacyUserInfoLength = 16 * 1024;

    [HttpGet("Login/{identity}")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> Login(string identity, bool keepMeSignedIn, bool sameSite, string primaryEmail = "", string? input = null, string? referer = null, bool isMobileApp = false)
        => await _server.Login(identity, keepMeSignedIn, sameSite, primaryEmail, input, referer, isMobileApp);

    [HttpGet("Login/CustomLogin")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> CustomLogin(Guid userId, bool keepMeSignedIn, string? referer = null, bool sameSite = false, bool isMobileApp = false, string? userInfo = null)
    {
        if (userId == Guid.Empty && TryReadLegacyUserId(userInfo, out Guid legacyUserId))
            return await server.CompleteLegacyTestLogin(
                legacyUserId,
                keepMeSignedIn,
                referer,
                isMobileApp);

        return await _server.CustomLogin(userId, keepMeSignedIn, referer, sameSite, isMobileApp);
    }

    [HttpPost("Login/TestSignIn")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
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
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> PasswordSignIn([FromForm] string email, [FromForm] string password, [FromForm] bool keepMeSignedIn = false, [FromForm] string? referer = null)
    {
        CloudLoginPasswordLoginRequest request = CloudLoginPasswordLoginRequest.Create(email, password, keepMeSignedIn);
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
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> PasswordRegistration([FromForm] string input, [FromForm] string inputFormat, [FromForm] string? password, [FromForm] string firstName, [FromForm] string lastName, [FromForm] string displayName, [FromForm] string? referer = null)
    {
        if (!Enum.TryParse<CloudLoginInputFormat>(inputFormat, true, out CloudLoginInputFormat format))
            return BadRequest("Invalid input format.");

        CloudLoginPasswordRegistrationRequest request = CloudLoginPasswordRegistrationRequest.Create(input, format, password, firstName, lastName, displayName);
        CloudUser user = await _server.PasswordRegistration(request);

        if (user is null)
            return BadRequest("Registration failed.");

        return Ok(CloudLoginTransportSecurity.ForTransport(user));
    }

    [HttpPost("Login/CodeRegistration")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> CodeRegistration([FromForm] string input, [FromForm] string inputFormat, [FromForm] string firstName, [FromForm] string lastName, [FromForm] string displayName, [FromForm] string? verificationToken = null, [FromForm] bool keepMeSignedIn = false, [FromForm] string? referer = null)
    {
        if (!Enum.TryParse(inputFormat, true, out CloudLoginInputFormat format))
            return BadRequest("Invalid input format.");

        CloudLoginCodeRegistrationRequest request = CloudLoginCodeRegistrationRequest.Create(
            input, format, firstName, lastName, displayName, verificationToken, keepMeSignedIn);

        try
        {
            CloudUser user = await _server.CodeRegistration(request);

            return Ok(CloudLoginTransportSecurity.ForTransport(user));
        }
        catch (UnauthorizedAccessException)
        {
            // The address was never proven, or the proof was already spent. Registration is the only
            // thing a verified code buys, so there is nothing else to fall back to.
            return Unauthorized();
        }
    }

    /// <summary>
    /// Issues a one-time code and delivers it to the address. The code is created here and never
    /// leaves the server - the caller receives only the handle it redeems against.
    /// </summary>
    [HttpPost("Login/Code/Send")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> SendCode([FromForm] string address, [FromForm] string? purpose = null)
    {
        if (!TryReadPurpose(purpose, out CloudLoginVerificationPurposes verificationPurpose))
            return BadRequest("Invalid verification purpose.");

        try
        {
            return Ok(await _server.SendVerificationCode(
                CloudLoginSendCodeRequest.Create(address, verificationPurpose)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// Redeems a code. The comparison, the attempt count and the sign-in that follows all happen
    /// here; the caller is told the outcome and nothing else.
    /// </summary>
    [HttpPost("Login/Code/Verify")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> VerifyCode([FromForm] string challengeId, [FromForm] string code, [FromForm] bool keepMeSignedIn = false)
    {
        try
        {
            return Ok(await _server.VerifyCode(
                CloudLoginVerifyCodeRequest.Create(challengeId, code, keepMeSignedIn)));
        }
        catch (UnauthorizedAccessException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static bool TryReadPurpose(string? value, out CloudLoginVerificationPurposes purpose)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            purpose = CloudLoginVerificationPurposes.SignIn;
            return true;
        }

        return Enum.TryParse(value, true, out purpose) && Enum.IsDefined(purpose);
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

    private static bool TryReadLegacyUserId(string? userInfo, out Guid userId)
    {
        userId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(userInfo) || userInfo.Length > MaximumLegacyUserInfoLength)
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(userInfo);

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    property.Value.ValueKind != JsonValueKind.String)
                    continue;

                return Guid.TryParse(property.Value.GetString(), out userId) && userId != Guid.Empty;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }
}
