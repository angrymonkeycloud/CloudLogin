using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AngryMonkey.Cloud.Login.Controllers
{
    [Route("CloudLogin")]
    [ApiController]
    public class LoginController : Controller
    {


        [HttpGet("Login/{identity}")]
         public async Task<ActionResult?> Login(string identity, string emailAddress, string redirectUri, bool KeepMeSignedIn)
        {
            AuthenticationProperties properties = new()
            {
                RedirectUri = redirectUri,
                ExpiresUtc = KeepMeSignedIn ? DateTimeOffset.UtcNow.AddMonths(3) : null,
                IsPersistent = KeepMeSignedIn
            };

            properties.SetParameter("login_hint", emailAddress);

            return identity.Trim().ToLower() switch
            {
                "microsoft" => Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme),
                "google" => Challenge(properties, GoogleDefaults.AuthenticationScheme),
                _ => null,
            };
            //await HttpContext.SignInAsync("Email",System.Security.Claims.ClaimsPrincipal.Current,properties);

            return Redirect("/");
        }

        [HttpGet("Login/CustomLogin")]
        public async Task<ActionResult<User>?> CustomLogin(User user)
        {
            //create a claim
            var claim = new Claim(ClaimTypes.Name,user.PrimaryEmailAddress.ToString());
            //create claimsIdentity
            var claimsIdentity = new ClaimsIdentity(new[] { claim }, "serverAuth");
            //create claimsPrincipal
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
            //Sign In User
            await HttpContext.SignInAsync(claimsPrincipal);

            return await Task.FromResult(user);
        }


        [HttpGet("Logout")]
        public async Task<ActionResult> Logout()
        {
            await HttpContext.SignOutAsync();

            return Redirect("/");
        }
    }
}
