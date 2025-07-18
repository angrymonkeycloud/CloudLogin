using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    //public ActionResult<CloudLoginClient> GetClient(string serverLoginUrl) => new CloudLoginClient()
    //{
    //    HttpServer = new() { BaseAddress = new(serverLoginUrl) },
    //    Providers = _configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.HandleUpdateOnly, key.Label)
    //    {
    //        IsCodeVerification = key.IsCodeVerification,
    //        HandlesPhoneNumber = key.HandlesPhoneNumber,
    //        HandlesEmailAddress = key.HandlesEmailAddress,
    //        HandleUpdateOnly = key.HandleUpdateOnly,
    //        InputRequired = key.InputRequired,
    //    }).ToList(),

    //    FooterLinks = _configuration.FooterLinks,
    //    RedirectUri = _configuration.RedirectUri
    //};

    public IActionResult Login(string identity, bool keepMeSignedIn, bool sameSite, string actionState, string primaryEmail = "", string? input = null, string? redirectUri = null)
    {
        // Validate redirect URI to prevent open redirect attacks
        if (!string.IsNullOrEmpty(redirectUri) && !CloudLoginShared.IsValidRedirectUri(redirectUri))
            throw new ArgumentException("Invalid redirect URI", nameof(redirectUri));

        RedirectParameters redirectParams = RedirectParameters.CreateCustomLogin("cloudlogin", "result", keepMeSignedIn, redirectUri, sameSite, actionState, primaryEmail);

        AuthenticationProperties globalProperties = new()
        {
            RedirectUri = CloudLoginShared.RedirectString(redirectParams)
        };

        if (!string.IsNullOrEmpty(input))
            globalProperties.SetParameter("login_hint", input);

        return _accessor.HttpContext?.User.Identity?.IsAuthenticated == true ? new RedirectResult(redirectUri ?? "/") : identity.Trim().ToLower() switch
        {
            "microsoft" => new ChallengeResult(MicrosoftAccountDefaults.AuthenticationScheme, globalProperties),
            "google" => new ChallengeResult(GoogleDefaults.AuthenticationScheme, globalProperties),
            "facebook" => new ChallengeResult(FacebookDefaults.AuthenticationScheme, globalProperties),
            "twitter" => new ChallengeResult(TwitterDefaults.AuthenticationScheme, globalProperties),
            _ => new NotFoundResult(),
        };
    }

    public async Task<IActionResult> CustomLogin(User user, bool keepMeSignedIn, string redirectUri = "", bool sameSite = false, string actionState = "", string primaryEmail = "")
    {
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";

        if (sameSite)
        {
            redirectUri = redirectUri.Replace($"{baseUrl}/", "");
            redirectUri = redirectUri.Replace($"/login", "");
        }

        //Dictionary<string, string> userDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(userInfo, CloudLoginSerialization.Options)!;

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn,
            RedirectUri = redirectUri
        };

        string firstName = user.FirstName;// userDictionary["FirstName"];
        string lastName = user.LastName; // userDictionary["LastName"];
        string displayName = user.DisplayName; // userDictionary["DisplayName"];
        string input = user.Inputs.First().Input;// userDictionary["Input"];

        if (_configuration.Cosmos == null)
        {
            firstName ??= "Guest";
            lastName ??= "User";
        }

        displayName ??= $"{firstName} {lastName}";

        ClaimsIdentity claimsIdentity = new([
                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim(ClaimTypes.Name, displayName)
            ], "CloudLogin");

        if (user.Inputs.First().Format == InputFormat.PhoneNumber)
            claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, input));

        if (user.Inputs.First().Format == InputFormat.EmailAddress)
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, input));

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        await _accessor.HttpContext!.SignInAsync(claimsPrincipal, properties);

        RedirectParameters redirectParams = RedirectParameters.CreateCustomLogin("cloudlogin", "result", keepMeSignedIn, redirectUri, sameSite, actionState, primaryEmail);

        return new RedirectResult(CloudLoginShared.RedirectString(redirectParams));
    }
    public async Task<IActionResult> LoginResult(bool keepMeSignedIn, bool sameSite, string? redirectUri = null, string actionState = "", string primaryEmail = "")
    {
        if (_cosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        ClaimsIdentity userIdentity = _request.HttpContext.User.Identities.First();
        string emailaddress = userIdentity.FindFirst(ClaimTypes.Email)?.Value!;

        User user = (_configuration.Cosmos != null ? await _cosmosMethods.GetUserByInput(emailaddress) : new()) ?? new();

        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";
        redirectUri ??= baseUrl;

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn
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
            return new RedirectResult(redirectUri);

        ClaimsIdentity claimsIdentity = new([
            new Claim(ClaimTypes.Hash, "CloudLogin"),
            new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
        ], "CloudLogin");

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        if (actionState == "AddInput")
        {
            LoginInput input = user.Inputs.First();
            string userInfo = JsonSerializer.Serialize(input, CloudLoginSerialization.Options);

            RedirectParameters redirectParams = RedirectParameters.Create("Actions", "AddInput");
            redirectParams = redirectParams with
            {
                RedirectUri = redirectUri,
                UserInfo = userInfo,
                PrimaryEmail = primaryEmail
            };

            return new RedirectResult(CloudLoginShared.RedirectString(redirectParams));
        }

        await _request.HttpContext.SignInAsync(claimsPrincipal, properties);

        if (actionState == "mobile")
            return new RedirectResult($"{baseUrl}/?actionState=mobile&redirectUri={redirectUri}");

        if (sameSite)
            return new RedirectResult(AddQueryString(redirectUri, $"KeepMeSignedIn={keepMeSignedIn}"));

        return new RedirectResult($"{redirectUri}/login?KeepMeSignedIn={keepMeSignedIn}");
    }

    private static string AddQueryString(string url, string queryString) =>
        $"{url}{(url.Contains('?') ? "&" : "?")}{queryString}";

    public async Task<IActionResult> UpdateAuth(string redirectUri, string? userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
            return new RedirectResult(redirectUri);

        _request.HttpContext.Response.Cookies.Delete("CloudLogin");
        _request.HttpContext.Response.Cookies.Append("CloudLogin", userInfo);

        return new RedirectResult(redirectUri ?? "/");
    }

    public async Task<IActionResult> Logout(string? redirectUri)
    {
        await _request.HttpContext.SignOutAsync();
        return new RedirectResult(!string.IsNullOrEmpty(redirectUri) ? redirectUri : "/");
    }
}
