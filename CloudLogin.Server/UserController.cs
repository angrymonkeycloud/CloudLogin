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
using System.Security.Principal;
using System.Net.Mail;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos.Linq;

namespace AngryMonkey.Cloud.Login.Controllers
{
    [Route("CloudLogin/User")]
    [ApiController]
    public class UserController : BaseController
    {
        [HttpGet("GetById")]
        public async Task<ActionResult<CloudUser>> GetById(Guid id)
        {
            try
            {
                CloudUser user = await CosmosMethods.GetUserById(id);
                return Ok(user);
            }
            catch
            {
                return Problem();
            }
        }

        [HttpGet("GetByEmailAddress")]
        public async Task<ActionResult<CloudUser>?> GetByEmailAddress(string emailAddress)
        {
            try
            {

                CloudUser? user = await CosmosMethods.GetUserByEmailAddress(emailAddress);

                return Ok(user);
            }
            catch(Exception e)
            {
                return Problem();
            }
        }

        [HttpGet("GetByPhoneNumber")]
        public async Task<ActionResult<CloudUser>?> GetByPhoneNumber(string phoneNumber)
        {
            try
            {
                CloudUser? user = await CosmosMethods.GetUserByPhoneNumber(phoneNumber);
                return Ok(user);
            }
            catch
            {
                return Problem();
            }
        }

        [HttpPost("SendWhatsAppCode")]
        public async Task<ActionResult> SendWhatsAppCode(string receiver, string code)
        {
            WhatsAppProviderConfiguration whatsAppProvider = Configuration.Providers.First(key => key is WhatsAppProviderConfiguration) as WhatsAppProviderConfiguration;

            string serialize = "{\"messaging_product\": \"whatsapp\",\"recipient_type\": \"individual\",\"to\": \"" + receiver.Replace("+", "") + "\",\"type\": \"template\",\"template\": {\"name\": \"" + whatsAppProvider.Template + "\",\"language\": {\"code\": \"" + whatsAppProvider.Language + "\"},\"components\": [{\"type\": \"body\",\"parameters\": [{\"type\": \"text\",\"text\": \"" + code + "\"}]}]}}";

            using HttpRequestMessage request = new()
            {
                Method = new HttpMethod("POST"),
                RequestUri = new(whatsAppProvider.RequestUri),
                Content = new StringContent(serialize),
            };

            request.Headers.Add("Authorization", whatsAppProvider.Authorization);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            try
            {
                HttpClient httpClient = new();

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return Ok();

                return Problem();
            }
            catch (Exception e)
            {
                return Problem();
            }
        }

        [HttpPost("SendEmailCode")]
        public async Task<ActionResult> SendEmailCode(string receiver, string code)
        {
            if (Configuration.EmailSendCodeRequest == null)
                return Problem("Email Code is not configured.");

            try
            {
                await Configuration.EmailSendCodeRequest.Invoke(new SendCodeValue(code, receiver));
                return Ok();
            }
            catch (Exception e)
            {
                return Problem(e.Message);
            }
        }

        [HttpPost("Update")]
        public async Task<ActionResult> Update([FromBody] CloudUser user)
        {
            try
            {
                await CosmosMethods.Container.UpsertItemAsync(user);
                return Ok();
            }
            catch
            {
                return Problem();
            }
        }

        [HttpPost("Create")]
        public async Task<ActionResult> Create([FromBody] CloudUser user)
        {
            try
            {
                await CosmosMethods.Container.CreateItemAsync(user);
                return Ok();
            }
            catch
            {
                return Problem();
            }
        }

        [HttpDelete("Delete")]
        public async Task<ActionResult> Delete(Guid userId)
        {
            try
            {
                await CosmosMethods.DeleteUser(userId);
                return Ok();
            }
            catch
            {
                return Problem();
            }
        }

        [HttpGet("All")]
        public async Task<ActionResult<List<CloudUser>>> All()
        {
            try
            {
                List<CloudUser> users = await CosmosMethods.GetUsers();
                return Ok(users);
            }
            catch
            {
                return Problem();
            }
		}

		[HttpGet("IsAuthenticated")]
		public async Task<ActionResult<bool>> IsAuthenticated()
		{
			try
			{
				string? userCookie = Request.Cookies["CloudLogin"];
				return Ok(userCookie != null);
			}
			catch
			{
				return Problem();
			}
		}

		[HttpGet("CurrentUser")]
		public async Task<ActionResult<CloudUser?>> CurrentUser()
		{
			try
			{
				string? userCookie = Request.Cookies["CloudUser"];

				if (userCookie == null)
					return Ok(null);

				CloudUser? user = JsonConvert.DeserializeObject<CloudUser>(userCookie);

				if (user == null)
					return Ok(null);

				return Ok(user);
			}
			catch
			{
				return Problem();
			}
		}
	}
}
