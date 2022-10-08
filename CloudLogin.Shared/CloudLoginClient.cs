using AngryMonkey.Cloud.Login.DataContract;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using static System.Net.WebRequestMethods;

namespace AngryMonkey.Cloud.Login
{
	public class CloudLoginClient
	{
		//public static CloudLoginConfiguration CloudLogin { get; set; }
		private HttpClient _httpClient;

		public HttpClient HttpClient
		{
			get
			{
				//return _httpClient ??= new HttpClient() { };
				return _httpClient;
			}
			set
			{
				_httpClient = value;
			}
		}
		public string? RedirectUrl { get; set; }
		public List<Link> FooterLinks { get; set; } = new();

		private CloudGeographyClient _cloudGepgraphy;

		private List<ProviderDefinition> _providers;
		public async Task<List<ProviderDefinition>> GetProviders() => _providers ??= await HttpClient.GetFromJsonAsync<List<ProviderDefinition>>("CloudLogin/Provider/All");

		public CloudGeographyClient CloudGeography => _cloudGepgraphy ??= new CloudGeographyClient();

		public async Task<List<CloudUser>> GetAllUsers()
		{
			return await HttpClient.GetFromJsonAsync<List<CloudUser>>("CloudLogin/User/All");
		}

		public async Task DeleteUser(Guid userId)
		{
			await HttpClient.DeleteAsync($"CloudLogin/User/Delete?id={userId}");
		}

		public async Task<CloudUser?> GetUserById(Guid userId)
		{
			return await HttpClient.GetFromJsonAsync<CloudUser>($"CloudLogin/User/GetById?id={HttpUtility.UrlEncode(userId.ToString())}");
		}

		public async Task<CloudUser?> GetUserByEmailAddress(string emailAddress)
		{
			try
			{
				return await HttpClient.GetFromJsonAsync<CloudUser>($"CloudLogin/User/GetByEmailAddress?emailAddress={HttpUtility.UrlEncode(emailAddress)}");
			}
			catch (Exception e)
			{
				Debugger.Break();
				throw e;
			}
		}

		public async Task<CloudUser?> GetUserByPhoneNumber(string phoneNumber)
		{
			return await HttpClient.GetFromJsonAsync<CloudUser>($"CloudLogin/User/GetByPhoneNumber?phoneNumber={HttpUtility.UrlEncode(phoneNumber)}");
		}

		public async Task SendWhatsAppCode(string receiver, string code)
		{
			await HttpClient.PostAsync($"CloudLogin/User/SendWhatsAppCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
		}

		public async Task SendEmailCode(string receiver, string code)
		{
			await HttpClient.PostAsync($"CloudLogin/User/SendEmailCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
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
			StringContent content = new(JsonConvert.SerializeObject(user), Encoding.UTF8, "application/json");

			await HttpClient.PostAsync("CloudLogin/User/Update", content);
		}

		public async Task CreateUser(CloudUser user)
		{
			StringContent content = new(JsonConvert.SerializeObject(user), Encoding.UTF8, "application/json");

			await HttpClient.PostAsync("CloudLogin/User/Create", content);
		}
	}
}
