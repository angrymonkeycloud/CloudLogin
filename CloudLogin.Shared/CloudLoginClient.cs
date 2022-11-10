using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;

namespace AngryMonkey.Cloud.Login
{
	public class CloudLoginClient
	{
		public HttpClient? HttpServer { get; set; }
		public string? RedirectUrl { get; set; }
		public List<Link>? FooterLinks { get; set; }
		public bool UsingDatabase { get; set; } = false;
		private CloudGeographyClient? _cloudGepgraphy;

		public List<ProviderDefinition> Providers { get; set; }

		public async Task<CloudLoginClient> InitFromServer()
		{
			CloudLoginClient client = null;

			try
			{
				client = await HttpServer.GetFromJsonAsync<CloudLoginClient>("CloudLogin/GetClient");
			}
			catch (Exception e)
			{
				throw e;
			}

			return client;
		}

		public async Task<bool> IsAuthenticated(IHttpContextAccessor? accessor = null)
		{
			if (accessor == null)
				try
				{
					HttpResponseMessage message = await HttpServer.GetAsync("CloudLogin/User/IsAuthenticated");

					if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
						return false;

					return await message.Content.ReadFromJsonAsync<bool>();
				}
				catch { throw; }

			string? userCookie = accessor.HttpContext.Request.Cookies["CloudLogin"];
			return userCookie != null;
		}

		public async Task<CloudUser?> CurrentUser(IHttpContextAccessor? accessor = null)
		{
			if (accessor == null)
				try
				{
					HttpResponseMessage message = await HttpServer.GetAsync("CloudLogin/User/CurrentUser");

					if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
						return null;

					return await message.Content.ReadFromJsonAsync<CloudUser>();
				}
				catch { throw; }

			string? userCookie = accessor.HttpContext.Request.Cookies["CloudUser"];

			if (userCookie == null)
				return null;

			return JsonConvert.DeserializeObject<CloudUser>(userCookie);
		}

		public CloudGeographyClient CloudGeography => _cloudGepgraphy ??= new CloudGeographyClient();

		public async Task<List<CloudUser>> GetAllUsers()
		{
			return await HttpServer.GetFromJsonAsync<List<CloudUser>>("CloudLogin/User/All");
		}

		public async Task DeleteUser(Guid userId)
		{
			await HttpServer.DeleteAsync($"CloudLogin/User/Delete?userId={userId}");
		}

		public async Task<List<CloudUser>?> GetUsersByDisplayName(string DisplayName)
		{
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(DisplayName)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            return await message.Content.ReadFromJsonAsync<List<CloudUser>?>();
        }
		public async Task<CloudUser?> GetUserById(Guid userId)
		{
			try
			{
				HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetById?id={HttpUtility.UrlEncode(userId.ToString())}");

				if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
					return null;

				return await message.Content.ReadFromJsonAsync<CloudUser?>();
			}
			catch
			{
				throw;
			}

		}

		public async Task<CloudUser?> GetUserByEmailAddress(string emailAddress)
		{
			try
			{
				HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetByEmailAddress?emailAddress={HttpUtility.UrlEncode(emailAddress)}"); ;

				if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
					return null;

				return await message.Content.ReadFromJsonAsync<CloudUser?>();
			}
			catch
			{
				throw;
			}
		}

		public async Task<CloudUser?> GetUserByPhoneNumber(string phoneNumber)
		{
			try
			{
				HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetByPhoneNumber?phoneNumber={HttpUtility.UrlEncode(phoneNumber)}");


				if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
					return null;

				return await message.Content.ReadFromJsonAsync<CloudUser?>();
			}
			catch
			{
				throw;
			}
		}

		public async Task SendWhatsAppCode(string receiver, string code)
		{
			await HttpServer.PostAsync($"CloudLogin/User/SendWhatsAppCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
		}

		public async Task SendEmailCode(string receiver, string code)
		{
			await HttpServer.PostAsync($"CloudLogin/User/SendEmailCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
		}

		public bool IsInputValidEmailAddress(string input) => Regex.IsMatch(input, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
		public bool IsInputValidPhoneNumber(string input) => CloudGeography.PhoneNumbers.IsValidPhoneNumber(input);

		public InputFormat GetInputFormat(string input)
		{
			if (string.IsNullOrEmpty(input))
				return InputFormat.Other;

			if (IsInputValidEmailAddress(input))
				return InputFormat.EmailAddress;

			if (IsInputValidPhoneNumber(input))
				return InputFormat.PhoneNumber;

			return InputFormat.Other;
		}

		public async Task<CloudUser?> GetUserByInput(string input) => GetInputFormat(input) switch
		{
			InputFormat.EmailAddress => await GetUserByEmailAddress(input),
			InputFormat.PhoneNumber => await GetUserByPhoneNumber(input),
			_ => null,
		};

		public async Task UpdateUser(CloudUser user)
		{
			HttpContent content = JsonContent.Create<CloudUser>(user);

			await HttpServer.PostAsync("CloudLogin/User/Update", content);
		}

		public async Task CreateUser(CloudUser user)
		{
			HttpContent content = JsonContent.Create<CloudUser>(user);

			await HttpServer.PostAsync("CloudLogin/User/Create", content);
		}
	}
}
