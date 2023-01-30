using System.Net.Http.Json;
using System.Web;
using AngryMonkey.Cloud;
using CloudLoginDataContract;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace LoginRequestLibrary;
public class CloudLoginClient
{

    public HttpClient? HttpServer { get; set; }
    public string? RedirectUrl { get; set; }

    private CloudGeographyClient? _cloudGepgraphy;


    public CloudGeographyClient CloudGeography => _cloudGepgraphy ??= new CloudGeographyClient();



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
    public async Task<CloudUser?> GetUserByDisplayName(string input)
    {
        return await GetUserByDisplayName(input);
    }
    public async Task<List<CloudUser>> GetAllUsers()
    {
        return await HttpServer.GetFromJsonAsync<List<CloudUser>>("CloudLogin/User/All");
    }
    public async Task<Guid> CreateUserRequest(Guid userId)
    {
        Guid requestId = Guid.NewGuid();

        await HttpServer.PostAsync($"CloudLogin/User/CreateRequest?userID={userId}&requestId={requestId}", null);

        return requestId;
    }
}