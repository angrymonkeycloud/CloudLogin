using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AngryMonkey.CloudLogin.API.Controllers;

/// <summary>
/// Self-service security endpoints for the signed-in user.
/// <para>
/// Every action requires authentication and none accepts a user id — the server resolves the
/// acting user from the session, so an authenticated caller can only ever read or change their
/// own security state.
/// </para>
/// </summary>
[Route("CloudLogin/Security")]
[ApiController]
[Authorize]
public class SecurityController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : CloudLoginBaseController(configuration, server)
{
    public sealed record DisconnectProviderBody(string ProviderCode, string Input);
    public sealed record CodeBody(string Code);
    public sealed record CompletePasskeyBody(string OptionsJson, string AttestationJson, string? Name);
    public sealed record CredentialBody(string CredentialId);
    public sealed record RenamePasskeyBody(string CredentialId, string Name);

    [HttpGet("Overview")]
    public async Task<ActionResult<CloudLoginSecurityOverview>> Overview()
    {
        try
        {
            return Ok(await _server.GetSecurityOverview());
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("LoginHistory")]
    public async Task<ActionResult<List<CloudLoginHistoryEntry>>> LoginHistory()
    {
        try
        {
            return Ok(await _server.GetMyLoginHistory());
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    /// <summary>The devices the signed-in user's account is signed in on.</summary>
    [HttpGet("Devices")]
    public async Task<ActionResult<List<CloudLoginSignedInDevice>>> Devices()
    {
        try
        {
            return Ok(await _server.GetMyDevices());
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    /// <summary>
    /// Signs one of the signed-in user's own devices out. Answers 404 for an id that is not
    /// theirs, which is indistinguishable from one that never existed.
    /// </summary>
    [HttpDelete("Devices/{deviceId}")]
    public async Task<ActionResult> SignOutDevice(string deviceId)
    {
        try
        {
            return await _server.SignOutMyDevice(deviceId) ? Ok() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    /// <summary>
    /// Signs every device except the current one out. Which one to keep is read from the
    /// caller's own ticket, never from the request.
    /// </summary>
    [HttpDelete("Devices")]
    public async Task<ActionResult<int>> SignOutOtherDevices()
    {
        try
        {
            return Ok(await _server.SignOutMyOtherDevices());
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("Password")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> Password([FromBody] CloudLoginChangePasswordRequest request)
    {
        try
        {
            await _server.ChangeMyPassword(request);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The message is user-facing guidance ("current password is incorrect"), not internal detail.
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("DisconnectProvider")]
    public async Task<IActionResult> DisconnectProvider([FromBody] DisconnectProviderBody body)
    {
        try
        {
            await _server.DisconnectProvider(body.ProviderCode, body.Input);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("Authenticator/Begin")]
    public async Task<ActionResult<CloudLoginAuthenticatorEnrollment>> BeginAuthenticator()
    {
        try
        {
            return Ok(await _server.BeginAuthenticatorEnrollment());
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("Authenticator/Confirm")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<bool>> ConfirmAuthenticator([FromBody] CodeBody body)
    {
        try
        {
            return Ok(await _server.ConfirmAuthenticatorEnrollment(body.Code));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("Authenticator/Disable")]
    public async Task<IActionResult> DisableAuthenticator()
    {
        try
        {
            await _server.DisableAuthenticator();
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("Passkeys/Begin")]
    public async Task<IActionResult> BeginPasskey()
    {
        try
        {
            // Returned as raw JSON: the browser hands this straight to navigator.credentials.create().
            return Content(await _server.BeginPasskeyRegistration(), "application/json");
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("Passkeys/Complete")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<CloudLoginPasskeySummary>> CompletePasskey([FromBody] CompletePasskeyBody body)
    {
        try
        {
            return Ok(await _server.CompletePasskeyRegistration(body.OptionsJson, body.AttestationJson, body.Name));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (Exception ex)
        {
            // Includes Fido2VerificationException: the attestation didn't verify.
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("Passkeys/Remove")]
    public async Task<IActionResult> RemovePasskey([FromBody] CredentialBody body)
    {
        try
        {
            await _server.RemovePasskey(body.CredentialId);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("Passkeys/Rename")]
    public async Task<IActionResult> RenamePasskey([FromBody] RenamePasskeyBody body)
    {
        try
        {
            await _server.RenamePasskey(body.CredentialId, body.Name);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }
}
