using CloudLoginDataContract;
using LoginRequestLibrary;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;

namespace AngryMonkey.Cloud.Login
{
	public class CloudLoginServerClient
	{
		public HttpClient? HttpServer { get; set; }
		public string? RedirectUrl { get; set; }
		public List<Link>? FooterLinks { get; set; }
        public List<ProviderDefinition> Providers { get; set; }
        public bool UsingDatabase { get; set; } = false;

		private CloudGeographyClient? _cloudGepgraphy;
        public CloudGeographyClient CloudGeography => _cloudGepgraphy ??= new CloudGeographyClient();


        private CloudLoginClient? _cloudLoginClient;
        public CloudLoginClient CloudLoginClient => _cloudLoginClient ??= new CloudLoginClient();


        public async Task<CloudLoginServerClient> InitFromServer()
        {
            CloudLoginServerClient client = null;

            try
            {
                HttpResponseMessage response = await HttpServer.GetAsync("CloudLogin/GetClient");

                if (!response.IsSuccessStatusCode)
                    throw await ThrowHttpException(response);

                client = await response.Content.ReadFromJsonAsync<CloudLoginServerClient>();
            }
            catch (Exception e)
            {
                throw e;
            }

            return client;
        }


        private static async Task<Exception> ThrowHttpException(HttpResponseMessage response)
        {
            try
            {
                HttpErrorResult? httpException = await response.Content.ReadFromJsonAsync<HttpErrorResult>();
                Exception test = new Exception(httpException == null ? "Unknown error" : httpException.Detail);
                return test;
            }
            catch (Exception ex)
            {
                string error = await response.Content.ReadAsStringAsync();
                return new Exception(error);
            }
        }

        private class HttpErrorResult
        {
            [JsonPropertyName("status")]
            public int Status { get; set; }
            [JsonPropertyName("detail")]
            public string Detail { get; set; }
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
        public async Task DeleteUser(Guid userId)
		{
			await HttpServer.DeleteAsync($"CloudLogin/User/Delete?userId={userId}");
        }
        public async Task AddInput(Guid userId, LoginInput Input)
        {

            HttpContent content = JsonContent.Create(Input);

            await HttpServer.PostAsync($"CloudLogin/User/AddInput?userId={userId}", content);
        }
		public async Task SendWhatsAppCode(string receiver, string code)
		{
			await HttpServer.PostAsync($"CloudLogin/User/SendWhatsAppCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
		}
		public async Task SendEmailCode(string receiver, string code)
		{
			await HttpServer.PostAsync($"CloudLogin/User/SendEmailCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
		}
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
            InputFormat.EmailAddress => await CloudLoginClient.GetUserByEmailAddress(input),
            InputFormat.PhoneNumber => await CloudLoginClient.GetUserByPhoneNumber(input),
            _ => null,
        };
        public bool IsInputValidEmailAddress(string input) => Regex.IsMatch(input, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
        public bool IsInputValidPhoneNumber(string input) => CloudGeography.PhoneNumbers.IsValidPhoneNumber(input);
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
