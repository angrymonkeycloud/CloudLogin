using System.Net.Http.Json;
using System.Web;

namespace AngryMonkey.CloudLogin;

public class CloudLoginClient
{
    public HttpClient? HttpServer { get; set; }
    public string? LoginUrl { get; set; }

    public CloudLoginClient(string baseAddress)
    {
        LoginUrl = baseAddress;
        HttpServer = new() { BaseAddress = new(baseAddress) };
    }

    public async Task<User?> GetUserById(Guid userId)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserById?id={HttpUtility.UrlEncode(userId.ToString())}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }
    }
    public async Task<List<User>?> GetAllUsers()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetAllUsers");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            List<User>? selectedUser = await message.Content.ReadFromJsonAsync<List<User>?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<List<User>?> GetUserByInput(string input)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserByInput?input={HttpUtility.UrlEncode(input)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            List<User>? selectedUser = await message.Content.ReadFromJsonAsync<List<User>?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<List<User>?> GetUserByPhoneNumber(string number)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserByPhoneNumber?number={HttpUtility.UrlEncode(number)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            List<User>? selectedUser = await message.Content.ReadFromJsonAsync<List<User>?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<List<User>?> GetUserByEmailAdress(string email)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserByEmailAdress?email={HttpUtility.UrlEncode(email)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            List<User>? selectedUser = await message.Content.ReadFromJsonAsync<List<User>?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<List<User>?> GetUsersByDisplayName(string displayName)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            List<User>? selectedUser = await message.Content.ReadFromJsonAsync<List<User>?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<User> GetUserByRequestId(Guid requestId, int minutesToExpiry)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserByRequestId?requestId={HttpUtility.UrlEncode(requestId.ToString())}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }
    }
    public async Task AddInput(Guid userId, LoginInput phoneNumber)
    {
        try
        {
            HttpContent content = JsonContent.Create(phoneNumber);

            HttpResponseMessage message = await HttpServer.PostAsync($"Api/Client/AddInput?userId={HttpUtility.UrlEncode(userId.ToString())}", content);

            return;
        }
        catch
        {
            throw;
        }
    }
}