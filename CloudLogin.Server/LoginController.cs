using System.Web;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using AngryMonkey.Cloud.Login.DataContract;
using AuthenticationProperties = Microsoft.AspNetCore.Authentication.AuthenticationProperties;

namespace AngryMonkey.Cloud.Login.Controllers
{
    [Route("CloudLogin")]
    [ApiController]
    public class LoginController : Controller
    {
        [HttpGet("Login/{identity}")]
        public async Task<ActionResult?> Login(string identity, string input, string redirectUri, bool keepMeSignedIn)
        {
            AuthenticationProperties properties = new()
            {
                RedirectUri = $"/cloudlogin/result?redirectUri={HttpUtility.UrlEncode(redirectUri)}",
                ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.AddMonths(3) : null,
                IsPersistent = keepMeSignedIn,
            };

            properties.SetParameter("login_hint", input);

            return identity.Trim().ToLower() switch
            {
                "microsoft" => Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme),
                "google" => Challenge(properties, GoogleDefaults.AuthenticationScheme),
                "facebook" => Challenge(properties, FacebookDefaults.AuthenticationScheme),
                "twitter" => Challenge(properties, TwitterDefaults.AuthenticationScheme),
                _ => null,
            };
        }

        [HttpGet("Login/CustomLogin")]
        public async Task<ActionResult<string>?> CustomLogin(string userInfo, bool keepMeSignedIn, string redirectUri)
        {
            Dictionary<string, string> userDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(userInfo);

            AuthenticationProperties properties = new()
            {
                ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.AddMonths(3) : null,
                IsPersistent = keepMeSignedIn
            };

            //create claimsIdentity
            var claimsIdentity = new ClaimsIdentity(new[] {

                new Claim(ClaimTypes.NameIdentifier, userDictionary["UserId"]),
                new Claim(ClaimTypes.GivenName, userDictionary["FirstName"]),
                new Claim(ClaimTypes.Surname, userDictionary["LastName"]),
                new Claim(ClaimTypes.Name, userDictionary["DisplayName"])

            }, ".");

            if (userDictionary["Type"].ToLower() == "phonenumber")
                claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, userDictionary["Input"]));
            if (userDictionary["Type"].ToLower() == "emailaddress")
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, userDictionary["Input"]));


            //create claimsPrincipal
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
            //Sign In User

            await HttpContext.SignInAsync(claimsPrincipal, properties);

            //await HttpContext.SignInAsync(claimsPrincipal, properties);

            return Redirect($"/cloudlogin/result?redirecturi={HttpUtility.UrlEncode(redirectUri)}");
        }

        [HttpGet("Result")]
        public async Task<ActionResult<string>> LoginResult(string redirectUri)
        {
            CloudUser user = JsonConvert.DeserializeObject<CloudUser>(HttpContext.Request.Cookies["CloudUser"]);

            AuthenticationProperties properties = new();

            //create claimsIdentity
            var claimsIdentity = new ClaimsIdentity(new[] {

                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.GivenName, user.FirstName),
                new Claim(ClaimTypes.Surname, user.LastName),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Hash, "Cloud Login")

            }, ".");
            if (user.EmailAddresses.FirstOrDefault() == null)
            {
                claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, user.PhoneNumbers.Where(key => key.IsPrimary == true).FirstOrDefault().Input));
            }else
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, user.EmailAddresses.Where(key=> key.IsPrimary == true).FirstOrDefault().Input));

            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(claimsPrincipal, properties);

            return Redirect(redirectUri);
        }

        [HttpGet("Logout")]
        public async Task<ActionResult> Logout()
        {
            await HttpContext.SignOutAsync();

            return Redirect("/");
        }
    }
}
