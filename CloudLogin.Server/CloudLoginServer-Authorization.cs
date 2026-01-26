using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Web;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    public IActionResult Login(string identity, bool keepMeSignedIn, bool sameSite, string primaryEmail = "", string? input = null, string? referer = null, bool isMobileApp = false)
    {
        // Validate referer to prevent open redirect attacks
        if (!string.IsNullOrEmpty(referer))
        {
            // Basic safety check - just block obviously dangerous schemes
            if (referer.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                referer.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                referer.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid referer", nameof(referer));
            }
        }

        // The OAuth provider redirect URI - this is fixed and configured in the OAuth provider
        // It should always point back to our CloudLogin service (NOT the external website)
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";
        string oauthRedirectUri = $"{baseUrl}/CloudLogin/Result";

        // Create authentication properties with the OAuth redirect URI
        AuthenticationProperties globalProperties = new()
        {
            RedirectUri = oauthRedirectUri // This is for OAuth providers
        };

        // Store the external website's URL in authentication properties for later use
        if (!string.IsNullOrEmpty(referer))
            globalProperties.Items["referer"] = referer;

        if (!string.IsNullOrEmpty(input))
            globalProperties.SetParameter("login_hint", input);

        if (isMobileApp)
            globalProperties.Items["isMobileApp"] = "true";

        if (keepMeSignedIn)
            globalProperties.Items["keepMeSignedIn"] = keepMeSignedIn.ToString();

        if (sameSite)
            globalProperties.Items["sameSite"] = "true";

        if (!string.IsNullOrEmpty(primaryEmail))
            globalProperties.Items["primaryEmail"] = primaryEmail;

        // If user is already authenticated, redirect appropriately
        if (_accessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            // If no external referer, go to account page
            if (string.IsNullOrEmpty(referer) || referer == "/" || referer == baseUrl || referer == $"{baseUrl}/")
                return new RedirectResult($"{baseUrl}/Account");

            return new RedirectResult(referer);
        }

        return identity.Trim().ToLower() switch
        {
            "microsoft" => new ChallengeResult(MicrosoftAccountDefaults.AuthenticationScheme, globalProperties),
            "google" => new ChallengeResult(GoogleDefaults.AuthenticationScheme, globalProperties),
            "facebook" => new ChallengeResult(FacebookDefaults.AuthenticationScheme, globalProperties),
            "twitter" => new ChallengeResult(TwitterDefaults.AuthenticationScheme, globalProperties),
            _ => new NotFoundResult(),
        };
    }

    public async Task<IActionResult> CustomLogin(UserModel user, bool keepMeSignedIn, string? referer = null, bool sameSite = false, bool isMobileApp = false)
    {
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";

        referer ??= string.Empty;

        if (sameSite)
        {
            referer = referer.Replace($"{baseUrl}/", "");
            referer = referer.Replace($"/login", "");
        }

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn,
            RedirectUri = referer
        };

        string firstName = user.FirstName ?? "Guest";
        string lastName = user.LastName ?? "User";
        string displayName = user.DisplayName ?? $"{firstName} {lastName}";
        string input = user.Inputs.FirstOrDefault()?.Input ?? "";

        ClaimsIdentity claimsIdentity = new([
                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim(ClaimTypes.Name, displayName),
                new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
            ], "CloudLogin");

        if (user.Inputs.FirstOrDefault()?.Format == InputFormat.PhoneNumber)
            claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, input));

        if (user.Inputs.FirstOrDefault()?.Format == InputFormat.EmailAddress)
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, input));

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        await _accessor.HttpContext!.SignInAsync(claimsPrincipal, properties);

        if (string.IsNullOrEmpty(referer))
            referer = "/";

        if (isMobileApp)
        {
            string separator = referer.Contains('?') ? "&" : "?";
            referer = $"{referer}{separator}isMobileApp=true";
        }

        return new RedirectResult(referer);
    }

    public async Task<IActionResult> LoginResult(bool keepMeSignedIn, bool sameSite, bool isMobileApp = false)
    {
        if (_cosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        ClaimsIdentity userIdentity = _request.HttpContext.User.Identities.First();
        string emailaddress = userIdentity.FindFirst(ClaimTypes.Email)?.Value!;

        UserModel user = (_configuration.Cosmos != null ? await _cosmosMethods.GetUserByInput(emailaddress) : new()) ?? new();

        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";

        // Try to get the referer (external website) from authentication properties
        string? referer = null;

        // Method 1: Try to get from authentication result
        AuthenticateResult authenticateResult = await _request.HttpContext.AuthenticateAsync();
        if (authenticateResult.Succeeded && authenticateResult.Properties?.Items != null)
        {
            if (authenticateResult.Properties.Items.TryGetValue("referer", out string? storedReferer))
                referer = storedReferer;
        }

        // Method 2: Try from HttpContext features if Method 1 failed
        if (string.IsNullOrEmpty(referer))
        {
            AuthenticateResult? authResult = _request.HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;

            if (authResult?.Properties?.Items != null && authResult.Properties.Items.TryGetValue("referer", out string? featureReferer))
                referer = featureReferer;
        }

        // Method 3: Check query parameters as fallback (legacy support)
        if (string.IsNullOrEmpty(referer))
            referer = _request.Query["referer"].FirstOrDefault() ?? _request.Query["referredUrl"].FirstOrDefault() ?? _request.Query["redirectUri"].FirstOrDefault();

        // Get other stored values
        string? storedIsMobileApp = null;
        string? storedKeepMeSignedIn = null;

        if (authenticateResult.Succeeded && authenticateResult.Properties?.Items != null)
        {
            authenticateResult.Properties.Items.TryGetValue("isMobileApp", out storedIsMobileApp);
            authenticateResult.Properties.Items.TryGetValue("keepMeSignedIn", out storedKeepMeSignedIn);
        }

        // Use stored values if available, otherwise use parameters
        bool finalIsMobileApp = !string.IsNullOrEmpty(storedIsMobileApp) ? bool.Parse(storedIsMobileApp) : isMobileApp;
        bool finalKeepMeSignedIn = !string.IsNullOrEmpty(storedKeepMeSignedIn) ? bool.Parse(storedKeepMeSignedIn) : keepMeSignedIn;


        if (!Uri.IsWellFormedUriString(referer, UriKind.Absolute))
            referer = HttpUtility.UrlDecode(referer);

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = finalKeepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null,
            IsPersistent = finalKeepMeSignedIn
        };

        string? firstName = user.FirstName ??= userIdentity.FindFirst(ClaimTypes.GivenName)?.Value;
        string? lastName = user.LastName ??= userIdentity.FindFirst(ClaimTypes.Surname)?.Value;
        string? displayName = user.DisplayName ??= $"{firstName} {lastName}";

        if (_configuration.Cosmos == null)
        {
            user = new()
            {
                DisplayName = displayName,
                FirstName = firstName,
                LastName = lastName,
                ID = Guid.NewGuid(),
                Inputs =
                [
                    new()
                    {
                        Format = InputFormat.EmailAddress,
                        Input = emailaddress,
                        IsPrimary = true
                    }
                ]
            };
        }

        if (user == null)
            return new RedirectResult(referer);

        // Create request ID for the external website
        Guid requestId = Guid.NewGuid();
        if (_configuration.Cosmos != null && user.ID != Guid.Empty)
            requestId = await CreateLoginRequest(user.ID);

        ClaimsIdentity claimsIdentity = new([
            new Claim(ClaimTypes.Hash, "CloudLogin"),
            new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
        ], "CloudLogin");

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        await _request.HttpContext.SignInAsync(claimsPrincipal, properties);


        // If no valid external referer, redirect to account page directly without request ID
        // Consider "/" or base URL as "no external referer"
        if (string.IsNullOrEmpty(referer) || referer == "/" || referer == baseUrl || referer == $"{baseUrl}/")
        {
            string accountUrl = $"{baseUrl}/Account";
            if (finalIsMobileApp)
            {
                string separator = accountUrl.Contains('?') ? "&" : "?";
                accountUrl = $"{accountUrl}{separator}isMobileApp=true";
            }
            return new RedirectResult(accountUrl);
        }

        // Build final redirect URL with user data for the external website
        string finalUrl;

        if (sameSite)
        {
            string keepMeSignedInParam = $"KeepMeSignedIn={finalKeepMeSignedIn}";
            string separator = referer.Contains('?') ? "&" : "?";
            finalUrl = $"{referer}{separator}{keepMeSignedInParam}&requestId={requestId}";

            if (finalIsMobileApp)
                finalUrl += "&isMobileApp=true";
        }
        else
        {
            // For external websites, add the user authentication data
            string separator = referer.Contains('?') ? "&" : "?";
            finalUrl = $"{referer}{separator}requestId={requestId}&keepMeSignedIn={finalKeepMeSignedIn}";

            if (finalIsMobileApp)
                finalUrl += "&isMobileApp=true";
        }

        return new RedirectResult(finalUrl);
    }

    private static string AddQueryString(string url, string queryString) =>
        $"{url}{(url.Contains('?') ? "&" : "?")}{queryString}";

    public async Task<IActionResult> UpdateAuth(string referer, string? userInfo, bool isMobileApp = false)
    {
        if (string.IsNullOrEmpty(userInfo))
        {
            if (isMobileApp)
            {
                string separator = referer.Contains('?') ? "&" : "?";
                referer = $"{referer}{separator}isMobileApp=true";
            }
            return new RedirectResult(referer);
        }

        _request.HttpContext.Response.Cookies.Delete("CloudLogin");
        _request.HttpContext.Response.Cookies.Append("CloudLogin", userInfo);

        string finalUrl = referer ?? "/";
        if (isMobileApp)
        {
            string separator = finalUrl.Contains('?') ? "&" : "?";
            finalUrl = $"{finalUrl}{separator}isMobileApp=true";
        }

        return new RedirectResult(finalUrl);
    }

    public async Task<IActionResult> Logout(string? referer, bool isMobileApp = false)
    {
        await _request.HttpContext.SignOutAsync();

        string logoutUrl = !string.IsNullOrEmpty(referer) ? referer : "/";
        if (isMobileApp)
        {
            string separator = logoutUrl.Contains('?') ? "&" : "?";
            logoutUrl = $"{logoutUrl}{separator}isMobileApp=true";
        }

        return new RedirectResult(logoutUrl);
    }
}
