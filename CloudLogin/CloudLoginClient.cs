using AngryMonkey.Cloud;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;


namespace AngryMonkey.CloudLogin;

public class CloudLoginClient
{
    public required HttpClient HttpServer { get; init; }
    public string LoginUrl => HttpServer.BaseAddress.AbsoluteUri;

    public string? RedirectUri { get; set; }
    public List<Link>? FooterLinks { get; set; }
    public List<ProviderDefinition> Providers { get; set; }
    public bool UsingDatabase { get; set; } = false;

    private CloudGeographyClient? _cloudGepgraphy;
    public CloudGeographyClient CloudGeography => _cloudGepgraphy ??= new CloudGeographyClient();

    //Misc
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

    public static bool IsInputValidEmailAddress(string input) => Regex.IsMatch(input, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
    public bool IsInputValidPhoneNumber(string input) => CloudGeography.PhoneNumbers.IsValidPhoneNumber(input);


    //Configuration
    public static async Task<CloudLoginClient> Build(string loginServerUrl)
    {
        HttpClient httpClient = new() {  BaseAddress = new(loginServerUrl) };

        return (await httpClient.GetFromJsonAsync<CloudLoginClient>($"CloudLogin/GetClient?serverLoginUrl={HttpUtility.UrlEncode(loginServerUrl)}"))!;
    }


    //Get user(s) information from db
    public async Task<List<User>?> GetAllUsers()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetAllUsers");

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
    public async Task<User?> GetUserById(Guid userId)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUserById?id={HttpUtility.UrlEncode(userId.ToString())}");

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
    public async Task<List<User>?> GetUsersByDisplayName(string displayName)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            List<User>? selectedUsers = await message.Content.ReadFromJsonAsync<List<User>?>();

            if (selectedUsers == null) return null;

            return selectedUsers;
        }
        catch
        {
            throw;
        }

    }
    public async Task<User?> GetUserByDisplayName(string displayName)
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUserByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

        if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

        User? selectedUser = await message.Content.ReadFromJsonAsync<User?>();

        if (selectedUser == null) return null;

        return selectedUser;
    }
    public async Task<User?> GetUserByInput(string input)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUserByInput?input={HttpUtility.UrlEncode(input)}");

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
    public async Task<User?> GetUserByEmailAddress(string email)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUserByEmailAdress?email={HttpUtility.UrlEncode(email)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent || message.StatusCode == System.Net.HttpStatusCode.InternalServerError) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<User?> GetUserByPhoneNumber(string number)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/User/GetUserByPhoneNumber?number={HttpUtility.UrlEncode(number)}");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent || message.StatusCode == System.Net.HttpStatusCode.InternalServerError) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>();

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }

    //Request based functions
    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Request/GetUserByRequestId?requestId={HttpUtility.UrlEncode(requestId.ToString())}");

            if (message.IsSuccessStatusCode)
                return await message.Content.ReadFromJsonAsync<User?>();

            return null;
        }
        catch
        {
            throw;
        }

    }
    public async Task<Guid?> CreateUserRequestCustom(Guid userId, Guid requestId)
    {
        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Request/CreateRequest?userID={userId}&requestId={requestId}", null);

        if (message.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return null;

        return requestId;
    }
    public async Task<Guid?> CreateUserRequest(Guid userId)
    {

        Guid requestId = Guid.NewGuid();

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Request/CreateRequest?userID={userId}&requestId={requestId}", null);

        if (message.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return null;

        return requestId;
    }

    //Code functions
    public async Task SendWhatsAppCode(string receiver, string code)
    {
        await HttpServer.PostAsync($"CloudLogin/User/SendWhatsAppCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
    }
    public async Task SendEmailCode(string receiver, string code)
    {
        await HttpServer.PostAsync($"CloudLogin/User/SendEmailCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
    }

    //User configuration
    public async Task UpdateUser(User user)
    {
        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync("CloudLogin/User/Update", content);
    }
    public async Task CreateUser(User user)
    {
        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync("CloudLogin/User/Create", content);
    }
    public async Task DeleteUser(Guid userId)
    {
        await HttpServer.DeleteAsync($"CloudLogin/User/Delete?userId={userId}");
    }
    public async Task<bool> AutomaticLogin()
    {

        return false;
    }
    public async Task<User?> CurrentUser()
    {
        //if (accessor == null)
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync("CloudLogin/User/CurrentUser");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            return await message.Content.ReadFromJsonAsync<User>();
        }
        catch
        {
            return null;
        }

        return null;
    }
    public async Task<bool> IsAuthenticated()
    {
        //if (accessor == null)
        try

        {
            HttpResponseMessage message = await HttpServer.GetAsync("CloudLogin/User/IsAuthenticated");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return false;

            return await message.Content.ReadFromJsonAsync<bool>();
        }
        catch { throw; }

        //string? userCookie = accessor.HttpContext.Request.Cookies["CloudLogin"];
        //return userCookie != null;
    }
    public async Task AddUserInput(Guid userId, LoginInput Input)
    {
        try
        {
            HttpContent content = JsonContent.Create(Input);

            HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/User/AddUserInput?userId={HttpUtility.UrlEncode(userId.ToString())}", content);

            return;
        }
        catch
        {
            throw;
        }
    }
}