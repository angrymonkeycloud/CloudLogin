using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Specialized;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Web;

namespace AngryMonkey.CloudLogin.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(IConfiguration configuration, ILogger<AuthController> logger) : ControllerBase
{
    private readonly string _loginBaseUrl = configuration["LoginUrl"] ?? throw new InvalidOperationException("LoginUrl configuration is missing.");
    private readonly ILogger<AuthController> _logger = logger;

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        try
        {
            returnUrl ??= "/";

            string? callbackUrl = Url.Action("Callback", "Auth", new { state = EncodeReturnUrl(returnUrl) }, Request.Scheme);

            NameValueCollection queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["referer"] = callbackUrl;

            string finalUrl = $"{_loginBaseUrl}?{queryParams}";

            return Redirect(finalUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login redirect");
            return BadRequest("Login redirect failed");
        }
    }

    [HttpGet("profile")]
    public IActionResult Profile([FromQuery] string? returnUrl = null)
    {
        try
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return Login($"/auth/profile?returnUrl={HttpUtility.UrlEncode(returnUrl ?? "/")}");

            returnUrl ??= "/";

            string? callbackUrl = Url.Action("ProfileCallback", "Auth", new { state = EncodeReturnUrl(returnUrl) }, Request.Scheme);

            string profileUrl = $"{_loginBaseUrl}/Account";

            NameValueCollection queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["referer"] = callbackUrl;

            string finalUrl = $"{profileUrl}?{queryParams}";

            return Redirect(finalUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during profile redirect");
            return BadRequest("Profile redirect failed");
        }
    }

    [HttpGet("profileCallback")]
    public IActionResult ProfileCallback([FromQuery] string? state)
    {
        try
        {
            return Redirect(DecodeReturnUrl(state));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during profile callback");
            return Redirect("/");
        }
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? requestId, [FromQuery] string? state, [FromQuery] string? error)
    {
        try
        {
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Login callback received error: {Error}", error);
                return Redirect("/?error=login_failed");
            }

            if (string.IsNullOrEmpty(requestId))
            {
                _logger.LogWarning("Login callback received without requestId");
                return Redirect("/?error=invalid_callback");
            }

            string returnUrl = DecodeReturnUrl(state);

            // Get user information from CloudLogin
            UserModel? user = await GetUserFromCloudLogin(requestId);

            if (user == null)
            {
                _logger.LogWarning("Failed to retrieve user information for requestId: {RequestId}", requestId);
                return Redirect("/?error=user_not_found");
            }

            // Create authentication claims
            List<Claim> claims =
            [
                new(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new(ClaimTypes.Name, user.DisplayName ?? user.FirstName ?? "User"),
                new(ClaimTypes.Email, user.PrimaryEmailAddress?.Input ?? ""),
                new("CloudLoginRequestId", requestId)
            ];

            if (!string.IsNullOrEmpty(user.FirstName))
                claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

            if (!string.IsNullOrEmpty(user.LastName))
                claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

            // Include profile picture in claims if available (OIDC-compatible "picture" claim)
            if (!string.IsNullOrWhiteSpace(user.ProfilePicture))
                claims.Add(new Claim("picture", user.ProfilePicture));

            // Optionally include country/locale if needed later
            if (!string.IsNullOrWhiteSpace(user.Country))
                claims.Add(new Claim("country", user.Country));
            if (!string.IsNullOrWhiteSpace(user.Locale))
                claims.Add(new Claim("locale", user.Locale));

            // Create authentication identity and principal
            ClaimsIdentity claimsIdentity = new(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

            // Sign in the user
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, new AuthenticationProperties
            {
                IsPersistent = true, // Keep user logged in across browser sessions
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) //30 days expiration
            });

            _logger.LogInformation("User {UserId} ({DisplayName}) authenticated successfully", user.ID, user.DisplayName);

            // Append requestId to return URL so WASM client can fetch and persist user
            string finalReturnUrl = AppendQueryParameter(returnUrl, "rid", requestId);

            return Redirect(finalReturnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login callback");
            return Redirect("/?error=callback_failed");
        }
    }

    [HttpPost("logout")]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout([FromQuery] string? returnUrl = null)
    {
        try
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            _logger.LogInformation("User logged out successfully");

            returnUrl ??= "/";

            // Build absolute return URL for the consumer website
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            string absoluteReturnUrl = returnUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? returnUrl
                : $"{baseUrl}{(returnUrl.StartsWith('/') ? "" : "/")}{returnUrl}";

            // Redirect to the standalone CloudLogin service logout to clear its session too,
            // otherwise the user remains signed in on the login service and cannot switch accounts.
            string cloudLoginLogoutUrl = $"{_loginBaseUrl.TrimEnd('/')}/CloudLogin/Logout?referer={Uri.EscapeDataString(absoluteReturnUrl)}";
            return Redirect(cloudLoginLogoutUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return Redirect("/");
        }
    }

    private async Task<UserModel?> GetUserFromCloudLogin(string requestId)
    {
        try
        {
            using HttpClient httpClient = new();
            httpClient.BaseAddress = new Uri(_loginBaseUrl);

            // Call CloudLogin API to get user by request ID
            HttpResponseMessage response = await httpClient.GetAsync($"/CloudLogin/Request/GetUserByRequestId?requestId={requestId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get user from CloudLogin. Status: {Status}", response.StatusCode);
                return null;
            }

            UserModel? user = await response.Content.ReadFromJsonAsync<UserModel>();
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user from CloudLogin");
            return null;
        }
    }

    private static string AppendQueryParameter(string url, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(url)) return $"?{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

        // Preserve any fragment by inserting the query before it
        int hashIdx = url.IndexOf('#');
        string beforeFragment = hashIdx >= 0 ? url[..hashIdx] : url;
        string fragment = hashIdx >= 0 ? url[hashIdx..] : string.Empty;

        string separator = beforeFragment.Contains('?') ? "&" : "?";
        return $"{beforeFragment}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}{fragment}";
    }

    private static string EncodeReturnUrl(string returnUrl)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(returnUrl));

    private string DecodeReturnUrl(string? state)
    {
        if (!string.IsNullOrEmpty(state))
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode state parameter");
            }
        }

        return "/";
    }
}