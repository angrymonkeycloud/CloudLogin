using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin/User")]
[ApiController]
public class UserController(CloudLoginWebConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpPost("SendWhatsAppCode")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult> SendWhatsAppCode(string receiver, string code)
    {
        if (!Configuration.Security.EnableLegacyClientVerificationCodes)
            return NotFound();

        try
        {
            await _server.SendWhatsAppCode(receiver, code);

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.ToString());
        }
    }

    [HttpPost("SendEmailCode")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<IActionResult> SendEmailCode(string receiver, string code)
    {
        if (!Configuration.Security.EnableLegacyClientVerificationCodes)
            return NotFound();

        try
        {
            await _server.SendEmailCode(receiver, code);

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    [HttpPost("Update")]
    [Authorize]
    public async Task<ActionResult> Update([FromBody] UserModel user)
    {
        try
        {
            UserModel? currentUser = await _server.CurrentUser();
            if (currentUser is null || (currentUser.ID != user.ID && !currentUser.IsGlobalAdmin))
                return Forbid();

            UserModel? storedUser = await _server.GetUserById(user.ID);
            if (storedUser is null)
                return NotFound();

            // Only profile fields are accepted here. Privileges, providers,
            // identifiers, password hashes, and lock state are server-managed.
            storedUser.FirstName = user.FirstName;
            storedUser.LastName = user.LastName;
            storedUser.DisplayName = user.DisplayName;
            storedUser.Username = user.Username;
            storedUser.DateOfBirth = user.DateOfBirth;
            storedUser.Country = user.Country;
            storedUser.Locale = user.Locale;

            await _server.UpdateUser(storedUser);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("Create")]
    [Authorize]
    public async Task<ActionResult> Create([FromBody] UserModel user)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            await _server.CreateUser(user);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("AddUserInput")]
    [Authorize]
    public async Task<ActionResult> AddInput(Guid userId, [FromBody] LoginInput Input)
    {
        try
        {
            if (!Configuration.Security.EnableLegacyClientVerificationCodes)
                return NotFound();

            if (!await CanAccessUserAsync(userId))
                return Forbid();

            await _server.AddUserInput(userId, Input);
            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("UploadProfilePicture")]
    [Authorize]
    public async Task<ActionResult<string>> UploadProfilePicture(Guid userId, [FromQuery] string contentType)
    {
        try
        {
            if (!await CanAccessUserAsync(userId))
                return Forbid();

            int maximumBytes = Configuration.Security.MaximumProfileImageBytes;
            if (Request.ContentLength is > 0 && Request.ContentLength > maximumBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            using MemoryStream ms = new(Math.Min(maximumBytes, 64 * 1024));
            byte[] buffer = new byte[64 * 1024];
            int bytesRead;
            while ((bytesRead = await Request.Body.ReadAsync(buffer, HttpContext.RequestAborted)) > 0)
            {
                if (ms.Length + bytesRead > maximumBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);

                await ms.WriteAsync(buffer.AsMemory(0, bytesRead), HttpContext.RequestAborted);
            }

            byte[] content = ms.ToArray();

            string url = await _server.UploadProfilePicture(userId, content, contentType);
            return Content(url, "text/plain");
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    [HttpDelete("Delete")]
    [Authorize]
    public async Task<ActionResult> Delete(Guid userId)
    {
        try
        {
            if (!await CanAccessUserAsync(userId))
                return Forbid();

            await _server.DeleteUser(userId);
            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("All")]
    [Authorize]
    public async Task<ActionResult<List<UserModel>>> All()
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            List<UserModel> users = await _server.GetAllUsers();
            users = [.. users.Select(NormalizeUser)];
            return Ok(users);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetAllUsers")]
    public async Task<ActionResult<List<UserModel>>> GetAllUsers()
    {
        try
        {
          
            List<UserModel> user = [.. (await _server.GetAllUsers()).Select(NormalizeUser)];

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetTestUsers")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<List<UserModel>>> GetTestUsers()
    {
        try
        {
            List<UserModel> users = await _server.GetTestUsers();
            users = [.. users.Select(NormalizeUser)];

            return Ok(users);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetUserById")]
    //[Authorize]
    public async Task<ActionResult<UserModel?>> GetUserById(Guid id)
    {
        try
        {
            //if (!await CanAccessUserAsync(id))
            //    return Forbid();

            UserModel? user = await _server.GetUserById(id);
            if (user is null)
                return NotFound();

            user = NormalizeUser(user);

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUsersByDisplayName")]
    [Authorize]
    public async Task<ActionResult<List<UserModel>>> GetUsersByDisplayName(string displayname)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            List<UserModel> user = [.. (await _server.GetUsersByDisplayName(displayname)).Select(NormalizeUser)];
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByDisplayName")]
    [Authorize]
    public async Task<ActionResult<UserModel?>> GetUserByDisplayName(string displayname)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            UserModel? user = await _server.GetUserByDisplayName(displayname);
            if (user is null)
                return NotFound();

            user = NormalizeUser(user);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByInput")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<UserModel>> GetUserByInput(string input)
    {
        try
        {
            UserModel? user = CloudLoginTransportSecurity.ForAnonymousDiscovery(
                await _server.GetUserByInput(input));

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByEmailAdress")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<UserModel>> GetUserByEmailAdress(string email)
    {
        try
        {
            UserModel? user = await _server.GetUserByEmailAddress(email);

            if (user == null)
                return NotFound();

            user = CloudLoginTransportSecurity.ForAnonymousDiscovery(user);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByPhoneNumber")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<UserModel>> GetUsersByPhoneNumber(string number)
    {
        try
        {
            UserModel? user = await _server.GetUserByPhoneNumber(number);

            if (user == null)
                return NotFound();

            user = CloudLoginTransportSecurity.ForAnonymousDiscovery(user);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("CurrentUser")]
    [Authorize]
    public async Task<ActionResult<UserModel?>> CurrentUser()
    {
        try
        {
            UserModel? user = await _server.CurrentUser();

            if (user == null)
                return NotFound();

            user = NormalizeUser(user);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("IsAuthenticated")]
    public async Task<ActionResult<bool>> IsAuthenticated()
    {
        try
        {
            bool isAuthenticated = await _server.IsAuthenticated();

            return Ok(isAuthenticated);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetUserCount")]
    [Authorize]
    public async Task<ActionResult<int>> GetUserCount()
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            return Ok(await _server.GetUserCount());
        }
        catch
        {
            return Problem();
        }
    }

    // ── Admin endpoints ───────────────────────────────────────────────

    [HttpPost("Admin/SetLocked")]
    [Authorize]
    public async Task<ActionResult> AdminSetLocked(Guid userId, bool locked)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            await _server.SetUserLocked(userId, locked);

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    [HttpPost("Admin/ResetPassword")]
    [Authorize]
    public async Task<ActionResult> AdminResetPassword(Guid userId, [FromBody] string newPassword)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            await _server.AdminResetPassword(userId, newPassword);

            return Ok();
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    [HttpPost("Admin/SetGlobalAdmin")]
    [Authorize]
    public async Task<ActionResult> AdminSetGlobalAdmin(Guid userId, bool isAdmin)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            await _server.SetGlobalAdmin(userId, isAdmin);

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    [HttpDelete("Admin/DeleteUser")]
    [Authorize]
    public async Task<ActionResult> AdminDeleteUser(Guid userId)
    {
        try
        {
            if (!await IsGlobalAdminAsync())
                return Forbid();

            await _server.DeleteUser(userId);

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    private async Task<bool> IsGlobalAdminAsync()
    {
        UserModel? currentUser = await _server.CurrentUser();

        return currentUser?.IsGlobalAdmin == true;
    }

    private async Task<bool> CanAccessUserAsync(Guid userId)
    {
        UserModel? currentUser = await _server.CurrentUser();
        return currentUser is not null && (currentUser.ID == userId || currentUser.IsGlobalAdmin);
    }

    private UserModel NormalizeUser(UserModel user)
    {
        user = CloudLoginTransportSecurity.ForTransport(user)!;
        if (!string.IsNullOrWhiteSpace(user.ProfilePicture))
            user.ProfilePicture = MakeAbsolute(user.ProfilePicture);
        if (!string.IsNullOrWhiteSpace(user.ProviderProfilePicture))
            user.ProviderProfilePicture = MakeAbsolute(user.ProviderProfilePicture);
        return user;
    }

    private string MakeAbsolute(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return value;

        string baseUrl = Configuration.AzureStorage?.PublicBaseUrl?.TrimEnd('/')!;

        string path = value.TrimStart('/');
        return new Uri(new Uri(baseUrl + "/"), path).ToString();
    }
}
