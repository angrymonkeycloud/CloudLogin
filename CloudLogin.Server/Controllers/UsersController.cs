using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Server.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(ILogger<UsersController> logger) : ControllerBase
{
    private readonly ILogger<UsersController> _logger = logger;

    [HttpGet("getUser")]
    public async Task<IActionResult> GetUser()
    {
        try
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Ok((UserModel?)null);
            }

            // Extract user information from claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var displayName = User.FindFirst(ClaimTypes.Name)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = User.FindFirst(ClaimTypes.GivenName)?.Value;
            var lastName = User.FindFirst(ClaimTypes.Surname)?.Value;
            var picture = User.FindFirst("picture")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Invalid or missing user ID in claims");
                return Ok((UserModel?)null);
            }

            // Create User object from claims
            var user = new UserModel
            {
                ID = userId,
                DisplayName = displayName,
                FirstName = firstName,
                LastName = lastName,
                ProfilePicture = picture,
                Inputs = []
            };

            // Add email to inputs if available
            if (!string.IsNullOrEmpty(email))
            {
                user.Inputs.Add(new LoginInput
                {
                    Input = email,
                    Format = InputFormat.EmailAddress,
                    IsPrimary = true,
                    Providers = []
                });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current user");
            return StatusCode(500, "Error retrieving user information");
        }
    }

    [HttpPost("logout")]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await HttpContext.SignOutAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, "Logout failed");
        }
    }

    [HttpGet("isAuthenticated")]
    public IActionResult IsAuthenticated()
    {
        return Ok(new { isAuthenticated = User.Identity?.IsAuthenticated ?? false });
    }
}