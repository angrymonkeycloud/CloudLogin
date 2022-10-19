using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login.DataContract;
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
using static System.Net.WebRequestMethods;

namespace AngryMonkey.Cloud.Login
{
    public class CloudLoginClient
    {
        private HttpClient _httpClient;
        public HttpClient HttpClient
        {
            get
            {
                return _httpClient;
            }
            set
            {
                _httpClient = value;
            }
        }
        public string? RedirectUrl { get; set; }
        public bool IsAuthenticated { get; set; }
        public CloudUser CurrentUser { get; set; }
        public List<Link> FooterLinks { get; set; } = new();
        public bool UsingDatabase { get; set; } = false;

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
            await HttpClient.DeleteAsync($"CloudLogin/User/Delete?userId={userId}");
        }

        public async Task<CloudUser?> GetUserById(Guid userId)
        {
            try
            {
                HttpResponseMessage message = await HttpClient.GetAsync($"CloudLogin/User/GetById?id={HttpUtility.UrlEncode(userId.ToString())}");

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
                HttpResponseMessage message = await HttpClient.GetAsync($"CloudLogin/User/GetByEmailAddress?emailAddress={HttpUtility.UrlEncode(emailAddress)}"); ;

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
                HttpResponseMessage message = await HttpClient.GetAsync($"CloudLogin/User/GetByPhoneNumber?phoneNumber={HttpUtility.UrlEncode(phoneNumber)}");


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
            HttpContent content = JsonContent.Create<CloudUser>(user);

            await HttpClient.PostAsync("CloudLogin/User/Update", content);
        }

        public async Task CreateUser(CloudUser user)
        {
            HttpContent content = JsonContent.Create<CloudUser>(user);

            await HttpClient.PostAsync("CloudLogin/User/Create", content);
        }
    }
}
