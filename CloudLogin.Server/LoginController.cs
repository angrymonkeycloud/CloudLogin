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
using Microsoft.Azure.Cosmos.Linq;
using System.Configuration;
using System.Net;
using Azure.Core;

namespace AngryMonkey.Cloud.Login.Controllers
{
    [Route("CloudLogin")]
    [ApiController]
    public class LoginController : BaseController
    {
        [HttpGet("GetClient")]
        public Task<CloudLoginClient> getClient()
        {
            CloudLoginClient client = new()
            {
                Providers = Configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.Label)
                {
                    IsCodeVerification = key.IsCodeVerification,
                    HandlesPhoneNumber = key.HandlesPhoneNumber,
                    HandlesEmailAddress = key.HandlesEmailAddress
                }).ToList(),
                FooterLinks = Configuration.FooterLinks,
                RedirectUrl = Configuration.RedirectUri
            };

            return Task.FromResult(client);
        }

        [HttpGet("Login/{identity}")]
        public async Task<ActionResult?> Login(string identity, string input, string redirectUri, bool keepMeSignedIn)
        {
            AuthenticationProperties globalProperties = new()
            {
                RedirectUri = $"/cloudlogin/result?redirectUri={HttpUtility.UrlEncode(redirectUri)}" +
                $"&ispersistent={HttpUtility.UrlEncode(keepMeSignedIn.ToString())}",
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
        public async Task<ActionResult<string>?> CustomLogin(string userInfo, bool keepMeSignedIn, string redirectUri = "")
        {
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

            }, ".");

            if (userDictionary["Type"].ToLower() == "phonenumber")
                claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, input));
            if (userDictionary["Type"].ToLower() == "emailaddress")
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, input));


            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(claimsPrincipal, properties);


            return Redirect($"/cloudlogin/result?redirecturi={HttpUtility.UrlEncode(redirectUri)}&ispersistent={keepMeSignedIn}");
        }

        [HttpGet("Result")]
        public async Task<ActionResult<string>> LoginResult(string redirectUri, string ispersistent)
        {
            CloudUser? user = JsonConvert.DeserializeObject<CloudUser>(HttpContext.Request.Cookies["CloudUser"]);

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

            }, ".");

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

            await HttpContext.SignInAsync(claimsPrincipal, newProperties);

            return Redirect(redirectUri);
        }

        [HttpGet("Logout")]
        public async Task<ActionResult> Logout()
        {
            await HttpContext.SignOutAsync();

            Response.Cookies.Delete("CloudUser");

            return Redirect("/");
        }
    }
}
