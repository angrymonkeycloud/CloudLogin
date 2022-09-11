using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Principal;

namespace AngryMonkey.Cloud.Login.Pages
{
	public class LoginModel : PageModel
	{
		public override ChallengeResult Challenge(AuthenticationProperties properties)
		{
			return base.Challenge(properties);
		}

		public void OnGet()
		{
			AuthenticationProperties properties = new()
			{
				
			};
		}

		public void OnGet(string redirectUri, string identity)
		{
			AuthenticationProperties properties = new()
			{
				RedirectUri = redirectUri
			};

			//return identity.Trim().ToLower() switch
			//{
			//	"microsoft" => Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme),
			//	"google" => Challenge(properties, GoogleDefaults.AuthenticationScheme),
			//	_ => Challenge(properties)
			//};
		}
	}
}
