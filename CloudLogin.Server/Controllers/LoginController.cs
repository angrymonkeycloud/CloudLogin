using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using AuthenticationProperties = Microsoft.AspNetCore.Authentication.AuthenticationProperties;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace AngryMonkey.CloudLogin;

[Route("CloudLogin")]
[ApiController]
public class LoginController(CloudLoginConfiguration configuration, CosmosMethods? cosmosMethods = null) : CloudLoginBaseController(configuration, cosmosMethods)
{
    [HttpGet("GetClient")]
    public ActionResult<CloudLoginClient> GetClient(string serverLoginUrl) => new CloudLoginClient()
    {
        HttpServer = new() { BaseAddress = new(serverLoginUrl) },
        Providers = Configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.HandleUpdateOnly, key.Label)
        {
            IsCodeVerification = key.IsCodeVerification,
            HandlesPhoneNumber = key.HandlesPhoneNumber,
            HandlesEmailAddress = key.HandlesEmailAddress,
            HandleUpdateOnly = key.HandleUpdateOnly,
            InputRequired = key.InputRequired,
        }).ToList(),

        FooterLinks = Configuration.FooterLinks,
        RedirectUri = Configuration.RedirectUri
    };

    [HttpGet("Login/{identity}")]
    public IActionResult Login(string identity, bool keepMeSignedIn, bool sameSite, string actionState, string primaryEmail = "", string? input = null, string? redirectUri = null)
    {
        AuthenticationProperties globalProperties = new()
        {
            RedirectUri = Methods.RedirectString("cloudlogin", "result", keepMeSignedIn: keepMeSignedIn.ToString(), redirectUri: redirectUri, sameSite: sameSite.ToString(), actionState: actionState, primaryEmail: primaryEmail)
        };

        if (!string.IsNullOrEmpty(input))
            globalProperties.SetParameter("login_hint", input);

        return identity.Trim().ToLower() switch
        {
            "microsoft" => Challenge(globalProperties, [MicrosoftAccountDefaults.AuthenticationScheme]),
            "google" => Challenge(globalProperties, [GoogleDefaults.AuthenticationScheme]),
            "facebook" => Challenge(globalProperties, [FacebookDefaults.AuthenticationScheme]),
            "twitter" => Challenge(globalProperties, [TwitterDefaults.AuthenticationScheme]),
            _ => NotFound(),
        };
    }

    [HttpGet("Login/CustomLogin")]
    public async Task<ActionResult<string>?> CustomLogin(string userInfo, bool keepMeSignedIn, string redirectUri = "", bool sameSite = false, string actionState = "", string primaryEmail = "")
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";
        if (sameSite)
        {
            redirectUri = redirectUri.Replace($"{baseUrl}/", "");
            redirectUri = redirectUri.Replace($"/login", "");
        }

        Dictionary<string, string> userDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(userInfo, CloudLoginSerialization.Options)!;

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(Configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn,
            RedirectUri = redirectUri
        };

        string firstName = userDictionary["FirstName"];
        string lastName = userDictionary["LastName"];
        string displayName = userDictionary["DisplayName"];
        string input = userDictionary["Input"];

        if (Configuration.Cosmos == null)
        {
            firstName ??= "Guest";
            lastName ??= "User";
        }

        displayName ??= $"{firstName} {lastName}";

        //create claimsIdentity
        var claimsIdentity = new ClaimsIdentity([

                new Claim(ClaimTypes.NameIdentifier, userDictionary["UserId"]),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim(ClaimTypes.Name, displayName)

            ], "CloudLogin");

        if (userDictionary["Type"].Equals("phonenumber", StringComparison.CurrentCultureIgnoreCase))
            claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, input));

        if (userDictionary["Type"].Equals("emailaddress", StringComparison.CurrentCultureIgnoreCase))
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, input));


        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        await HttpContext.SignInAsync(claimsPrincipal, properties);

        return Redirect(Methods.RedirectString("cloudlogin", "result", keepMeSignedIn: keepMeSignedIn.ToString(), sameSite: sameSite.ToString(), redirectUri: redirectUri, actionState: actionState, primaryEmail: primaryEmail));
    }

    [HttpGet("Result")]
    public async Task<IActionResult> LoginResult(bool keepMeSignedIn, bool sameSite, string? redirectUri = null, string actionState = "", string primaryEmail = "")
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        //try
        //{
        ClaimsIdentity userIdentity = Request.HttpContext.User.Identities.First();

        string emailaddress = userIdentity.FindFirst(ClaimTypes.Email)?.Value!;

        User user = (Configuration.Cosmos != null ? CosmosMethods.GetUserByInput(emailaddress).Result : new()) ?? new();

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        redirectUri ??= baseUrl;

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(Configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn
        };

        string? firstName = user.FirstName ??= userIdentity.FindFirst(ClaimTypes.GivenName)?.Value;
        string? lastName = user.LastName ??= userIdentity.FindFirst(ClaimTypes.Surname)?.Value;
        string? displayName = user.DisplayName ??= $"{firstName} {lastName}";

        if (Configuration.Cosmos == null)
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

        if (user == null)
            return Redirect(redirectUri);

        ClaimsIdentity claimsIdentity = new([
                new Claim(ClaimTypes.Hash, "CloudLogin"),
                new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
            ], "CloudLogin");

        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        if (actionState == "AddInput")
        {
            LoginInput input = user.Inputs.First();
            string userInfo = JsonSerializer.Serialize(input, CloudLoginSerialization.Options);

            return Redirect(Methods.RedirectString("Actions", "AddInput", redirectUri: redirectUri, userInfo: userInfo, primaryEmail: primaryEmail));
        }

        await HttpContext.SignInAsync(claimsPrincipal, properties);

        if (actionState == "mobile")
            return Redirect($"{baseUrl}/?actionState=mobile&redirectUri={redirectUri}");

        if (sameSite)
            return Redirect(AddQueryString(redirectUri, $"KeepMeSignedIn={keepMeSignedIn}"));

        return Redirect($"{redirectUri}/login?KeepMeSignedIn={keepMeSignedIn}");
        //}
        //catch (Exception ex)
        //{
        //    await EmailService.SendEmail("Exception from Result (LoginController)", ex.ToString(), ["elietebchrani@live.com"]);

        //    return Results.Problem(ex.ToString());
        //}
    }

    private static string AddQueryString(string url, string queryString) => $"{url}{(url.Contains('?') ? "&" : "?")}{queryString}";


    [HttpGet("Update")]
    public IActionResult Update(string redirectUri, string? userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
            return Redirect(redirectUri);

        Response.Cookies.Delete("CloudLogin");

        Response.Cookies.Append("CloudLogin", userInfo);

        if (redirectUri == null)
            return Redirect("/");

        return Redirect(redirectUri);
    }

    [HttpGet("Logout")]
    public async Task<IActionResult> Logout(string? redirectUri)
    {
        await HttpContext.SignOutAsync();

        if (!string.IsNullOrEmpty(redirectUri))
            return Redirect(redirectUri);

        return Redirect("/");
    }


}