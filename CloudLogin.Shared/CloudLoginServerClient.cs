using AngryMonkey.Cloud;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace AngryMonkey.CloudLogin
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

        public async Task<CloudLoginServerClient> InitFromServer()
        {
            CloudLoginServerClient client = null;

            try
            {
                HttpResponseMessage response = await HttpServer.GetAsync("CloudLogin/GetClient");

                client = await response.Content.ReadFromJsonAsync<CloudLoginServerClient>();
            }
            catch (Exception e)
            {
                throw e;
            }

            return client;
        }
        public async Task<User?> GetUserByEmailAddress(string emailAddress)
        {
            try
            {
                HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetByEmailAddress?emailAddress={HttpUtility.UrlEncode(emailAddress)}"); ;

                if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null;

                return await message.Content.ReadFromJsonAsync<User?>();
            }
            catch
            {
                throw;
            }
        }
        public async Task<User?> GetUserByPhoneNumber(string phoneNumber)
        {
            try
            {
                HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetByPhoneNumber?phoneNumber={HttpUtility.UrlEncode(phoneNumber)}");


                if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null;

                return await message.Content.ReadFromJsonAsync<User?>();
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
        public async Task<User?> GetUserByInput(string input) => GetInputFormat(input) switch
        {
            InputFormat.EmailAddress => await GetUserByEmailAddress(input),
            InputFormat.PhoneNumber => await GetUserByPhoneNumber(input),
            _ => null,
        };
        public bool IsInputValidEmailAddress(string input) => Regex.IsMatch(input, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
        public bool IsInputValidPhoneNumber(string input) => CloudGeography.PhoneNumbers.IsValidPhoneNumber(input);
        public async Task UpdateUser(User user)
		{
			HttpContent content = JsonContent.Create<User>(user);

			await HttpServer.PostAsync("CloudLogin/User/Update", content);
		}
		public async Task CreateUser(User user)
		{
			HttpContent content = JsonContent.Create<User>(user);

			await HttpServer.PostAsync("CloudLogin/User/Create", content);
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
        public async Task<User?> CurrentUser(IHttpContextAccessor? accessor = null)
        {
            if (accessor == null)
                try
                {
                    HttpResponseMessage message = await HttpServer.GetAsync("CloudLogin/User/CurrentUser");

                    if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                        return null;

                    return await message.Content.ReadFromJsonAsync<User>();
                }
                catch { throw; }

            string? userCookie = accessor.HttpContext.Request.Cookies["User"];

            if (userCookie == null)
                return null;

            return JsonConvert.DeserializeObject<User>(userCookie);
        }
        public async Task<User?> GetUserFromRequest(Guid requestId)
        {
            try
            {
                HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUserFromRequest?requestId={HttpUtility.UrlEncode(requestId.ToString())}");

                if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null;

                return await message.Content.ReadFromJsonAsync<User?>();
            }
            catch
            {
                throw;
            }

        }
        public async Task<List<User>?> GetUsersByDisplayName(string DisplayName)
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(DisplayName)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            return await message.Content.ReadFromJsonAsync<List<User>?>();
        }
        public async Task<User?> GetUserById(Guid userId)
        {
            try
            {
                HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetById?id={HttpUtility.UrlEncode(userId.ToString())}");

                if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null;

                return await message.Content.ReadFromJsonAsync<User?>();
            }
            catch
            {
                throw;
            }

        }
        public async Task<User?> GetUserByDisplayName(string input)
        {
            return await GetUserByDisplayName(input);
        }
        public async Task<List<User>> GetAllUsers()
        {
            return await HttpServer.GetFromJsonAsync<List<User>>("CloudLogin/User/All");
        }
        public async Task<Guid> CreateUserRequest(Guid userId)
        {
            Guid requestId = Guid.NewGuid();

            await HttpServer.PostAsync($"CloudLogin/User/CreateRequest?userID={userId}&requestId={requestId}", null);

            return requestId;
        }
    }
}
