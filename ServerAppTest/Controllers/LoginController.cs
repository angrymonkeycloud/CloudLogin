using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AngryMonkey.Cloud.Login.Controllers
{
	[Route("CloudLogin")]
	[ApiController]
	public class LoginController : Controller
	{
		[HttpGet("Login/{identity}")]
		public async Task<ActionResult?> Login(string identity, string emailAddress, string redirectUri)
		{
			AuthenticationProperties properties = new()
			{
				RedirectUri = redirectUri
			};

			properties.SetParameter("login_hint", emailAddress);

			return identity.Trim().ToLower() switch
			{
				"microsoft" => Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme),
				"google" => Challenge(properties, GoogleDefaults.AuthenticationScheme),
				_ => null,
			};
		}

		[HttpGet("Logout")]
		public async Task<ActionResult> Logout()
		{
			await HttpContext.SignOutAsync();

			return Redirect("/");
		}
	}
}
