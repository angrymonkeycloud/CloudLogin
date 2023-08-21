using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using AuthenticationProperties = Microsoft.AspNetCore.Authentication.AuthenticationProperties;
using System.Linq.Expressions;

namespace AngryMonkey.CloudLogin;

[Route("CloudLogin")]
[ApiController]
public class LoginController : BaseController
{
    public Methods Methods = new Methods();
    [HttpGet("GetClient")]
    public ActionResult<CloudLoginClient> GetClient()
    {
        CloudLoginClient client = CloudLoginClient.InitializeForServer();

        client.Providers = Configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.HandleUpdateOnly, key.Label)
        {
            IsCodeVerification = key.IsCodeVerification,
            HandlesPhoneNumber = key.HandlesPhoneNumber,
            HandlesEmailAddress = key.HandlesEmailAddress,
            HandleUpdateOnly = key.HandleUpdateOnly,
            InputRequired = key.InputRequired,
        }).ToList();
        client.FooterLinks = Configuration.FooterLinks;
        client.RedirectUri = Configuration.RedirectUri;

        return client;
    }

    [HttpGet("Login/{identity}")]
    public async Task<ActionResult?> Login(string identity, bool keepMeSignedIn, bool sameSite, string actionState, string primaryEmail = "", string? input = null, string? redirectUri = null)
    {
        AuthenticationProperties globalProperties = new()
        {
            RedirectUri = Methods.RedirectString("cloudlogin", "result", keepMeSignedIn: keepMeSignedIn.ToString(), redirectUri: redirectUri, sameSite: sameSite.ToString(), actionState: actionState, primaryEmail: primaryEmail)
        };

        if (!string.IsNullOrEmpty(input))
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

        return Redirect(Methods.RedirectString("cloudlogin", "result",  keepMeSignedIn: keepMeSignedIn.ToString(), sameSite: sameSite.ToString(), redirectUri: redirectUri, actionState: actionState, primaryEmail: primaryEmail));
    }

    [HttpGet("Result")]
    public async Task<ActionResult<string>> LoginResult(bool keepMeSignedIn, bool sameSite, string? redirectUri = null, string actionState = "", string primaryEmail = "")
    {
        ClaimsIdentity userIdentity = Request.HttpContext.User.Identities.First();

        User? user = new();

        if (Configuration.Cosmos != null)
             user = CosmosMethods.GetUserByInput(userIdentity.FindFirst(ClaimTypes.Email)?.Value!).Result;

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";
            
        redirectUri ??= baseUrl;

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(Configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn
        };


        string firstName = user.FirstName ??= userIdentity.FindFirst(ClaimTypes.GivenName)?.Value;
        string lastName = user.LastName ??= userIdentity.FindFirst(ClaimTypes.Surname)?.Value;
        string emailaddress = userIdentity.FindFirst(ClaimTypes.Email)?.Value;
        string  displayName = user.DisplayName ??= $"{firstName} {lastName}";

        if (Configuration.Cosmos == null)
            user = new()
            {
                DisplayName = displayName,
                FirstName = firstName,
                LastName = lastName,
                ID = Guid.NewGuid(),
                Inputs = new()
                {
                    new()
                    {
                        Format = InputFormat.EmailAddress,
                        Input = emailaddress,
                        IsPrimary = true
                    }
                }
            };


        if (user == null)
            return Redirect(redirectUri);





        ClaimsIdentity claimsIdentity = new(new[] {
                //new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                //new Claim(ClaimTypes.GivenName, firstName),
                //new Claim(ClaimTypes.Surname, lastName),
                //new Claim(ClaimTypes.Name, displayName),
                new Claim(ClaimTypes.Hash, "CloudLogin"),
                new Claim(ClaimTypes.UserData, JsonConvert.SerializeObject(user))
            }, "CloudLogin");

        //if (!user.EmailAddresses.Any())
        //    if (user.PhoneNumbers.Any())
        //        claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, user.PhoneNumbers.First(key => key.IsPrimary == true).Input));
        //    else
        //        claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, displayName));

        //else
        //    if (user.EmailAddresses.Any())
        //    claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, user.EmailAddresses.First(key => key.IsPrimary == true).Input));
        //else
        //    claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, displayName));

        
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        if (actionState == "AddInput")
        {
            LoginInput input = user.Inputs.First();
            string userInfo = JsonConvert.SerializeObject(input);

            return Redirect(Methods.RedirectString("Actions", "AddInput", redirectUri: redirectUri, userInfo: userInfo, primaryEmail: primaryEmail));
        }

        await HttpContext.SignInAsync(claimsPrincipal, properties);

        if (sameSite)
            return Redirect($"{redirectUri}");
        else
            return Redirect($"{redirectUri}/login");
    }


    [HttpGet("Update")]
    public async Task<ActionResult> Update(string? redirectUri, string userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
            return Redirect(redirectUri);

        Response.Cookies.Delete("CloudLogin");

        //Request.Cookies.TryGetValue("LoggedInUser", out string? LoggedInUserValue);


        //if (!string.IsNullOrEmpty(LoggedInUserValue))
        //{
        //    Console.WriteLine("going in");
        //    User? user = JsonConvert.DeserializeObject<User>(userInfo);

        //    Response.Cookies.Delete("LoggedInUser");

        //    User? loggedInValue = JsonConvert.DeserializeObject<User>(LoggedInUserValue);

        //    loggedInValue.FirstName = user.FirstName;
        //    loggedInValue.LastName = user.LastName;
        //    loggedInValue.DisplayName = user.DisplayName;
        //    loggedInValue.IsLocked = user.IsLocked;
        //    loggedInValue.ID = user.ID;
        //    string loggedIn = JsonConvert.SerializeObject(loggedInValue);

        //    Response.Cookies.Append("LoggedInUser", loggedIn);
        //}

        Response.Cookies.Append("CloudLogin", userInfo);

        if (redirectUri == null)
            return Redirect("/");

        return Redirect(redirectUri);
    }

    [HttpGet("Logout")]
    public async Task<ActionResult> Logout(string? redirectUri)
    {
            await HttpContext.SignOutAsync();


        if (!string.IsNullOrEmpty(redirectUri))
            return Redirect(redirectUri);

        return Redirect("/");
    }


}