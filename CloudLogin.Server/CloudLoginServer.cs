using System.Web;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    /// <summary>
    /// Handles the final login result and creates the authenticated session
    /// </summary>
    public async Task<IActionResult> LoginResult(HttpRequest request, HttpResponse response, Guid requestId, string? currentUser, string? returnUrl, bool keepMeSignedIn, bool isMobileApp = false)
    {
        const string separator = "?";
        string actualSeparator = LoginUrl.Contains('?') ? "&" : separator;

        returnUrl ??= $"{request.Scheme}://{request.Host}";

        User? cloudUser = await ResolveUserFromRequest(requestId, currentUser);

        if (cloudUser == null)
            return Login(request, returnUrl, isMobileApp);

        await CreateAuthenticatedSession(request.HttpContext, cloudUser, keepMeSignedIn);
        
        CleanupLoginCookies(response, keepMeSignedIn);

        // Add mobile app parameter to return URL if needed
        if (isMobileApp)
        {
            string separator2 = returnUrl.Contains('?') ? "&" : "?";
            returnUrl = $"{returnUrl}{separator2}isMobileApp=true";
        }

        return new RedirectResult(returnUrl);
    }

    /// <summary>
    /// Handles automatic login for returning users
    /// </summary>
    public ActionResult<bool> AutomaticLogin(HttpRequest request, bool isMobileApp = false)
    {
        try
        {
            string separator = LoginUrl.Contains('?') ? "&" : "?";
            string? userCookie = request.Cookies["AutomaticSignIn"];

            if (userCookie == null)
                return new OkObjectResult(false);

            string redirectUrl = $"{LoginUrl}{separator}Account/Login";
            
            if (isMobileApp)
            {
                string mobileSeparator = redirectUrl.Contains('?') ? "&" : "?";
                redirectUrl = $"{redirectUrl}{mobileSeparator}isMobileApp=true";
            }
            
            return new RedirectResult(redirectUrl);
        }
        catch
        {
            return new ObjectResult("An error occurred while attempting automatic login.") { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Handles user logout and cleanup
    /// </summary>
    public async Task<string> Logout(HttpRequest request, HttpResponse response, bool isMobileApp = false)
    {
        await request.HttpContext.SignOutAsync();

        response.Cookies.Delete("AutomaticSignIn");
        string baseUri = $"{request.Scheme}://{request.Host}";
        string separator = LoginUrl.Contains('?') ? "&" : "?";
        
        string logoutUrl = $"{LoginUrl}CloudLogin/Logout{separator}redirectUri={HttpUtility.UrlEncode(baseUri)}";
        
        if (isMobileApp)
        {
            string mobileSeparator = logoutUrl.Contains('?') ? "&" : "?";
            logoutUrl = $"{logoutUrl}{mobileSeparator}isMobileApp=true";
        }
        
        return logoutUrl;
    }

    /// <summary>
    /// Updated Login method to handle mobile app parameter
    /// </summary>
    public IActionResult Login(HttpRequest request, string? returnUrl, bool isMobileApp = false)
    {
        string baseUrl = $"{request.Scheme}://{request.Host}";

        string separator = LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(returnUrl))
            returnUrl = baseUrl;

        string redirectUri = $"{baseUrl}/Account/LoginResult?ReturnUrl={HttpUtility.UrlEncode(returnUrl)}";
        
        if (isMobileApp)
            redirectUri += "&isMobileApp=true";

        string finalUrl = $"{LoginUrl}{separator}redirectUri={HttpUtility.UrlEncode(redirectUri)}";
        
        if (isMobileApp)
        {
            string mobileSeparator = finalUrl.Contains('?') ? "&" : "?";
            finalUrl = $"{finalUrl}{mobileSeparator}isMobileApp=true";
        }

        return new RedirectResult(finalUrl);
    }

    #region Private Helper Methods

    /// <summary>
    /// Resolves user from either request ID or serialized user data
    /// </summary>
    private async Task<User?> ResolveUserFromRequest(Guid requestId, string? currentUser)
    {
        if (requestId != Guid.Empty)
            return await GetUserByRequestId(requestId);

        if (!string.IsNullOrEmpty(currentUser))
            return JsonSerializer.Deserialize<User>(currentUser, CloudLoginSerialization.Options);

        return null;
    }

    /// <summary>
    /// Creates an authenticated session for the user
    /// </summary>
    private static async Task CreateAuthenticatedSession(HttpContext context, User user, bool keepMeSignedIn)
    {
        ClaimsIdentity claimsIdentity = new(
        [
            new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new Claim(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
            new Claim(ClaimTypes.Surname, user.LastName ?? string.Empty),
            new Claim(ClaimTypes.Name, user.DisplayName ?? string.Empty),
            new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
        ], "CloudLogin");

        // Add email claim - prefer primary email, fallback to first input
        string emailClaim = user.PrimaryEmailAddress?.Input ?? user.Inputs.FirstOrDefault()?.Input ?? string.Empty;
        if (!string.IsNullOrEmpty(emailClaim))
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, emailClaim));

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.AddMonths(6) : null,
            IsPersistent = keepMeSignedIn
        };

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, properties);
    }

    /// <summary>
    /// Cleans up login-related cookies
    /// </summary>
    private static void CleanupLoginCookies(HttpResponse response, bool keepMeSignedIn)
    {
        response.Cookies.Delete("LoggingIn");

        if (keepMeSignedIn)
            response.Cookies.Append("AutomaticSignIn", "True", new CookieOptions { 
                Expires = DateTimeOffset.UtcNow.AddMonths(6),
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });
    }

    #endregion
}
