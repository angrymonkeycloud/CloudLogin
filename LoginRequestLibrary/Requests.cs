using AngryMonkey.Cloud.Login.DataContract;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace LoginRequestLibrary
{
    public class Requests
    {

        public HttpClient? HttpServer { get; set; }
        public string? RedirectUrl { get; set; }

        public async Task<CloudUser?> GetRequestFromDB(Guid requestId)
        {
            try
            {
                HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetById?id={HttpUtility.UrlEncode(requestId.ToString())}");

                if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null;

                return await message.Content.ReadFromJsonAsync<CloudUser?>();
            }
            catch
            {
                throw;
            }

        }
    }

}