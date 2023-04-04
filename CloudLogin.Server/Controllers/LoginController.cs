using System.Web;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using AuthenticationProperties = Microsoft.AspNetCore.Authentication.AuthenticationProperties;

namespace AngryMonkey.CloudLogin;

[Route("CloudLogin")]
[ApiController]
public class LoginController : BaseController
{
    [HttpGet("GetClient")]
    public ActionResult<CloudLoginClient> GetClient()
    {
        CloudLoginClient client = CloudLoginClient.InitializeForServer();

        client.Providers = Configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.HandleUpdateOnly, key.Label)
        {
            IsCodeVerification = key.IsCodeVerification,
            HandlesPhoneNumber = key.HandlesPhoneNumber,
            HandlesEmailAddress = key.HandlesEmailAddress,
            HandleUpdateOnly = key.HandleUpdateOnly
        }).ToList();
        client.FooterLinks = Configuration.FooterLinks;
        client.RedirectUrl = Configuration.RedirectUri;

        return client;
    }

    [HttpGet("Login/{identity}")]
    public async Task<ActionResult?> Login(string identity, string input, string redirectUri, bool keepMeSignedIn, bool sameSite, string actionState, string primaryEmail = "")
    {

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";


        AuthenticationProperties globalProperties = new()
        {
            RedirectUri = $"/cloudlogin/result?redirectUri={HttpUtility.UrlEncode(redirectUri)}" +
            $"&ispersistent={HttpUtility.UrlEncode(keepMeSignedIn.ToString())}&samesite={HttpUtility.UrlEncode(sameSite.ToString())}&actionstate={HttpUtility.UrlEncode(actionState)}&primaryEmail={primaryEmail}",
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(Configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn,
        };

        globalProperties.SetParameter("login_hint", input);

        return identity.Trim().ToLower() switch
        {
            "microsoft" => Challenge(globalProperties, MicrosoftAccountDefaults.AuthenticationScheme),
            "google" => Challenge(globalProperties, GoogleDefaults.AuthenticationScheme),
            "facebook" => Challenge(globalProperties, FacebookDefaults.AuthenticationScheme),
            "twitter" => Challenge(globalProperties, TwitterDefaults.AuthenticationScheme),
            _ => null,
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


        Dictionary<string, string> userDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(userInfo);

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
        var claimsIdentity = new ClaimsIdentity(new[] {

                new Claim(ClaimTypes.NameIdentifier, userDictionary["UserId"]),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim(ClaimTypes.Name, displayName)

            }, "CloudLogin");

        if (userDictionary["Type"].ToLower() == "phonenumber")
            claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, input));
        if (userDictionary["Type"].ToLower() == "emailaddress")
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, input));


        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        await HttpContext.SignInAsync(claimsPrincipal, properties);

        return Redirect($"/cloudlogin/result?redirecturi={HttpUtility.UrlEncode(redirectUri)}&ispersistent={keepMeSignedIn}&samesite={HttpUtility.UrlEncode(sameSite.ToString())}&actionState={HttpUtility.UrlEncode(actionState)}&primaryEmail={primaryEmail}");
    }

    [HttpGet("Result")]
    public async Task<ActionResult<string>> LoginResult(string ispersistent, string sameSite, string? redirectUri = "", string actionState = "", string primaryEmail = "")
    {
        User? user = JsonConvert.DeserializeObject<User>(HttpContext.Request.Cookies["User"]);

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";


        if (user == null)
            return Redirect(redirectUri);

        AuthenticationProperties newProperties = new()
        {
            IsPersistent = ispersistent == "True",
            ExpiresUtc = ispersistent == "True" ? DateTimeOffset.UtcNow.Add(Configuration.LoginDuration) : null
        };

        string firstName = user.FirstName;
        string lastName = user.LastName;
        string? displayName = user.DisplayName;

        if (Configuration.Cosmos == null)
        {
            firstName ??= "Guest";
            lastName ??= "User";
            user = new()
            {
                DisplayName = $"{firstName} {lastName}",
                FirstName = firstName,
                LastName = lastName,
                ID = Guid.NewGuid()
            };
        }

        displayName ??= $"{firstName} {lastName}";
        var claimsIdentity = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim(ClaimTypes.Name, displayName),
                new Claim(ClaimTypes.Hash, "Cloud Login")
            }, "CloudLogin");

        if (user.EmailAddresses.FirstOrDefault() == null)
            if (user.PhoneNumbers.FirstOrDefault() != null)
                claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, user.PhoneNumbers.Where(key => key.IsPrimary == true).FirstOrDefault().Input));
            else
                claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, displayName));

        else
            if (user.EmailAddresses.FirstOrDefault() != null)
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, user.EmailAddresses.Where(key => key.IsPrimary == true).FirstOrDefault().Input));
        else
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, displayName));


        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        if (actionState == "AddInput")
        {
            LoginInput input = user.Inputs.FirstOrDefault();
            string userInfo = JsonConvert.SerializeObject(input);
            return Redirect($"Actions/AddInput?domainName={HttpUtility.UrlEncode(redirectUri)}&userInfo={HttpUtility.UrlEncode(userInfo)}&primaryEmail={primaryEmail}");
        }

        await HttpContext.SignInAsync(claimsPrincipal, newProperties);

        if (sameSite == "True")
            return Redirect($"{redirectUri}");
        else
            return Redirect($"{redirectUri}/login");
    }

    [HttpGet("Update")]
    public async Task<ActionResult> Logout(string? redirectUrl, string userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
            return Redirect(redirectUrl);

        Response.Cookies.Delete("User");

        Request.Cookies.TryGetValue("LoggedInUser", out string? LoggedInUserValue);


        if (!string.IsNullOrEmpty(LoggedInUserValue))
        {
            Console.WriteLine("going in");
            User? user = JsonConvert.DeserializeObject<User>(userInfo);

            Response.Cookies.Delete("LoggedInUser");

            User? loggedInValue = JsonConvert.DeserializeObject<User>(LoggedInUserValue);

            loggedInValue.FirstName = user.FirstName;
            loggedInValue.LastName = user.LastName;
            loggedInValue.DisplayName = user.DisplayName;
            loggedInValue.IsLocked = user.IsLocked;
            loggedInValue.ID = user.ID;
            string loggedIn = JsonConvert.SerializeObject(loggedInValue);

            Response.Cookies.Append("LoggedInUser", loggedIn);
        }

        Response.Cookies.Append("User", userInfo);

        if (redirectUrl == null)
            return Redirect("/");

        return Redirect(redirectUrl);
    }

    [HttpGet("Logout")]
    public async Task<ActionResult> Logout(string? redirectUrl)
    {
        await HttpContext.SignOutAsync();

        Response.Cookies.Delete("User");
        Response.Cookies.Delete("LoggedInUser");

        if (!string.IsNullOrEmpty(redirectUrl))
            return Redirect(redirectUrl);

        return Redirect("/");
    }
}