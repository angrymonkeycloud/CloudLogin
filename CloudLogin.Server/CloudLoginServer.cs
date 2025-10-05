using System.Web;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    /// <summary>
    /// Finalizes login: creates authenticated session then redirects.
    /// If a referer/returnUrl is provided it appends the one-time requestId (if not already present) and redirects there.
    /// If not provided, redirects to the local /Account page.
    /// </summary>
    public async Task<IActionResult> LoginResult(HttpRequest request, HttpResponse response, Guid requestId, string? currentUser, string? returnUrl, bool keepMeSignedIn, bool _ = false)
    {
        // Resolve user from requestId or serialized user payload
        User? cloudUser = await ResolveUserFromRequest(requestId, currentUser);
        if (cloudUser == null)
            return Login(request, returnUrl, false); // Restart login flow if user vanished (e.g. expired request)

        await CreateAuthenticatedSession(request.HttpContext, cloudUser, keepMeSignedIn);
        CleanupLoginCookies(response, keepMeSignedIn);

        // If no returnUrl (referer) -> go to account page on host
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            string localAccount = $"{request.Scheme}://{request.Host}/Account";
            return new RedirectResult(localAccount);
        }

        // Decode if encoded
        try { returnUrl = HttpUtility.UrlDecode(returnUrl); } catch { }

        // Append requestId only if not already present
        if (!returnUrl.Contains("requestId=", StringComparison.OrdinalIgnoreCase))
            returnUrl = AppendQuery(returnUrl, "requestId", requestId.ToString());

        return new RedirectResult(returnUrl);
    }

    /// <summary>
    /// Automatic login (unchanged logic except mobile flag removed)
    /// </summary>
    public ActionResult<bool> AutomaticLogin(HttpRequest request, bool _ = false)
    {
        try
        {
            string? userCookie = request.Cookies["AutomaticSignIn"];
            if (userCookie == null)
                return new OkObjectResult(false);

            // Redirect to standard login (will auto-complete)
            string redirectUrl = $"{LoginUrl}{(LoginUrl.Contains('?') ? '&' : '?')}Account/Login";
            return new RedirectResult(redirectUrl);
        }
        catch
        {
            return new ObjectResult("An error occurred while attempting automatic login.") { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Logout: sign out then redirect back to base host (or provided referer if desired externally).
    /// </summary>
    public async Task<string> Logout(HttpRequest request, HttpResponse response, bool _ = false)
    {
        await request.HttpContext.SignOutAsync();
        response.Cookies.Delete("AutomaticSignIn");
        string baseUri = $"{request.Scheme}://{request.Host}";
        string logoutUrl = $"{LoginUrl}CloudLogin/Logout{(LoginUrl.Contains('?') ? '&' : '?')}redirectUri={HttpUtility.UrlEncode(baseUri)}";
        return logoutUrl;
    }

    /// <summary>
    /// Entry point to start login. If a referer query parameter is present it is treated as the final destination.
    /// A requestId will be appended to that referer after successful authentication.
    /// </summary>
    public IActionResult Login(HttpRequest request, string? returnUrl, bool _ = false)
    {
        string baseUrl = $"{request.Scheme}://{request.Host}";

        // Prefer explicit referer query parameter (custom scheme OR https OR any absolute target)
        string? referer = request.Query["referer"];
        if (!string.IsNullOrWhiteSpace(referer))
        {
            try { referer = HttpUtility.UrlDecode(referer); } catch { }
            if (!string.IsNullOrWhiteSpace(referer))
                returnUrl = referer;
        }

        // Fallbacks
        if (string.IsNullOrWhiteSpace(returnUrl))
            returnUrl = baseUrl; // Root of host if nothing supplied

        // Build redirect URI that CloudLogin will call after provider auth
        string loginResultRedirect = $"{baseUrl}/Account/LoginResult?ReturnUrl={HttpUtility.UrlEncode(returnUrl)}";

        // Compose final external provider bootstrap URL
        string finalUrl = $"{LoginUrl}{(LoginUrl.Contains('?') ? '&' : '?')}redirectUri={HttpUtility.UrlEncode(loginResultRedirect)}";

        return new RedirectResult(finalUrl);
    }

    #region Private Helper Methods

    private static string AppendQuery(string url, string key, string value)
    {
        if (string.IsNullOrEmpty(url)) return url;

        // Preserve fragment (#...)
        string? fragment = null;
        int hashIndex = url.IndexOf('#');
        if (hashIndex >= 0)
        {
            fragment = url.Substring(hashIndex);
            url = url.Substring(0, hashIndex);
        }

        char separator = url.Contains('?') ? '&' : '?';
        url = $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

        if (!string.IsNullOrEmpty(fragment))
            url += fragment;

        return url;
    }

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
