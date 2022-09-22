using AngryMonkey.Cloud.Components.Icons;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor.Internal;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Configuration;
using System.Net.Mail;
using System.Security.Claims;

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
				RedirectUri = redirectUri,
				ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.AddMonths(3) : null,
				IsPersistent = keepMeSignedIn,
			};

			properties.SetParameter("login_hint", input);

			return identity.Trim().ToLower() switch
			{
				"microsoft" => Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme),
				"google" => Challenge(properties, GoogleDefaults.AuthenticationScheme),
				"facebook" => Challenge(properties, FacebookDefaults.AuthenticationScheme),
				_ => null,
			};
		}

		[HttpGet("Login/CustomLogin")]
		public async Task<ActionResult<string>?> CustomLogin(string userInfo, bool keepMeSignedIn)
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

			}, userDictionary["Type"]);

			if (userDictionary["Type"].ToLower() == "phonenumber")
				claimsIdentity.AddClaim(new Claim(ClaimTypes.MobilePhone, userDictionary["Input"]));
			if (userDictionary["Type"].ToLower() == "email")
				claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, userDictionary["Input"]));


            //create claimsPrincipal
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
			//Sign In User

			await HttpContext.SignInAsync(claimsPrincipal, properties);

			//await HttpContext.SignInAsync(claimsPrincipal, properties);

			return Redirect("/");
		}


		[HttpGet("Logout")]
		public async Task<ActionResult> Logout()
		{
			await HttpContext.SignOutAsync();

			return Redirect("/");
		}
	}
}
