//using System.Web;
//using Newtonsoft.Json;
//using Microsoft.AspNetCore.Mvc;
//using System.Security.Claims;
//using Microsoft.AspNetCore.Authentication;
//using Microsoft.AspNetCore.Authentication.Facebook;
//using Microsoft.AspNetCore.Authentication.Google;
//using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
//using Microsoft.AspNetCore.Authentication.Twitter;
//using AngryMonkey.Cloud.Login.DataContract;
//using AuthenticationProperties = Microsoft.AspNetCore.Authentication.AuthenticationProperties;
//using System.Security.Principal;
//using System.Net.Mail;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using Microsoft.Extensions.Options;

//namespace AngryMonkey.Cloud.Login.Controllers
//{
//	[Route("CloudLogin/Provider")]
//	[ApiController]
//	public class ProviderController : BaseController
//	{
//		[HttpGet("All")]
//		public async Task<ActionResult<List<ProviderDefinition>>> All()
//		{
//			try
//			{
//				List<ProviderDefinition> providers = Configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.Label)
//				{
//					IsCodeVerification = key.IsCodeVerification,
//					HandlesPhoneNumber = key.HandlesPhoneNumber,
//					HandlesEmailAddress = key.HandlesEmailAddress
//				}).ToList();

//				return Ok(providers);
//			}
//			catch
//			{
//				return Problem();
//			}
//		}
//	}
//}
