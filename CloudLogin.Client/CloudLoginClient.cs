using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;


namespace AngryMonkey.CloudLogin;

public class CloudLoginClient : ICloudLogin
{
    public required HttpClient HttpServer { get; init; }
    public string LoginUrl => HttpServer.BaseAddress!.AbsoluteUri;

    public string UserRoute = "CloudLogin/User";
    public string? RedirectUri { get; set; }
    public List<Link>? FooterLinks { get; set; }

    public async Task<List<ProviderDefinition>> GetProviders()
    {
        HttpResponseMessage response = await HttpServer.GetAsync("api/providers");

        Console.WriteLine(LoginUrl);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to get providers. Status code: {response.StatusCode}");

        string responseContent = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrEmpty(responseContent))
        {
            // Handle empty response
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ProviderDefinition>>(responseContent, CloudLoginSerialization.Options);
        }
        catch (JsonException ex)
        {
            // Log the response content and the exception
            Console.WriteLine($"Failed to deserialize response: {responseContent}");
            Console.WriteLine(ex);
            throw;
        }
    }

    public bool UsingDatabase { get; set; } = true;

    private static CloudGeographyClient? _cloudGepgraphy;
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
    //public static async Task<CloudLoginClient> Build(string loginServerUrl)
    //{
    //    HttpClient httpClient = new() { BaseAddress = new(loginServerUrl) };

    //    return (await httpClient.GetFromJsonAsync<CloudLoginClient>($"CloudLogin/GetClient?serverLoginUrl={HttpUtility.UrlEncode(loginServerUrl)}", CloudLoginSerialization.Options))!;
    //}

    // Was in StandAlone, which is Light Version now.
    public async Task<bool> AutomaticLogin()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/AutomaticLogin");

            if (message.StatusCode == HttpStatusCode.NoContent)
                return false;

            return await message.Content.ReadFromJsonAsync<bool>(CloudLoginSerialization.Options);
        }
        catch { throw; }
    }

    //Get user(s) information from db
    public async Task<List<User>?> GetAllUsers()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetAllUsers");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            List<User>? selectedUser = await message.Content.ReadFromJsonAsync<List<User>?>(CloudLoginSerialization.Options);

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
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserById?id={HttpUtility.UrlEncode(userId.ToString())}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>(CloudLoginSerialization.Options);

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
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            List<User>? selectedUsers = await message.Content.ReadFromJsonAsync<List<User>?>(CloudLoginSerialization.Options);

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
        if (!UsingDatabase)
            return null;

        HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

        if (message.StatusCode == HttpStatusCode.NoContent) return null;

        User? selectedUser = await message.Content.ReadFromJsonAsync<User?>(CloudLoginSerialization.Options);

        if (selectedUser == null) return null;

        return selectedUser;
    }
    public async Task<User?> GetUserByInput(string input)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByInput?input={HttpUtility.UrlEncode(input)}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>(CloudLoginSerialization.Options);

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
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByEmailAdress?email={HttpUtility.UrlEncode(email)}");

            if (message.StatusCode == HttpStatusCode.NoContent || message.StatusCode == HttpStatusCode.InternalServerError) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>(CloudLoginSerialization.Options);

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
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByPhoneNumber?number={HttpUtility.UrlEncode(number)}");

            if (message.StatusCode == HttpStatusCode.NoContent || message.StatusCode == HttpStatusCode.InternalServerError) return null;

            User? selectedUser = await message.Content.ReadFromJsonAsync<User?>(CloudLoginSerialization.Options);

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
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Request/GetUserByRequestId?requestId={HttpUtility.UrlEncode(requestId.ToString())}");

            if (message.IsSuccessStatusCode)
                return await message.Content.ReadFromJsonAsync<User?>(CloudLoginSerialization.Options);

            return null;
        }
        catch
        {
            throw;
        }

    }
    public async Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null)
    {
        if (!UsingDatabase)
            throw new Exception("Database is not enabled.");

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Request/CreateRequest?userID={userId}&requestId={requestId}", null);

        if (message.StatusCode == HttpStatusCode.BadRequest)
            throw new Exception("Request creation failed.");

        string guidString = await message.Content.ReadAsStringAsync();

        return new Guid(guidString);
    }

    //Code functions
    public async Task SendWhatsAppCode(string receiver, string code)
    {
        await HttpServer.PostAsync($"{UserRoute}/SendWhatsAppCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
    }
    public async Task SendEmailCode(string receiver, string code)
    {
        await HttpServer.PostAsync($"{UserRoute}/SendEmailCode?receiver={HttpUtility.UrlEncode(receiver)}&code={HttpUtility.UrlEncode(code)}", null);
    }

    //User configuration
    public async Task UpdateUser(User user)
    {
        if (!UsingDatabase)
            return;

        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync($"{UserRoute}/Update", content);
    }
    public async Task CreateUser(User user)
    {
        if (!UsingDatabase)
            return;

        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync($"{UserRoute}/Create", content);
    }
    public async Task DeleteUser(Guid userId)
    {
        if (!UsingDatabase)
            return;

        await HttpServer.DeleteAsync($"{UserRoute}/Delete?userId={userId}");
    }
    public async Task<User?> CurrentUser()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/CurrentUser");

            if (message.StatusCode == HttpStatusCode.NoContent)
                return null;

            return await message.Content.ReadFromJsonAsync<User>(CloudLoginSerialization.Options);
        }
        catch
        {
            return null;
        }
    }
    public async Task<bool> IsAuthenticated()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/IsAuthenticated");

            if (message.StatusCode == HttpStatusCode.NoContent)
                return false;

            return await message.Content.ReadFromJsonAsync<bool>(CloudLoginSerialization.Options);
        }
        catch { return false; }
    }
    public async Task AddUserInput(Guid userId, LoginInput Input)
    {
        if (!UsingDatabase)
            return;

        try
        {
            HttpContent content = JsonContent.Create(Input);

            HttpResponseMessage message = await HttpServer.PostAsync($"{UserRoute}/AddUserInput?userId={HttpUtility.UrlEncode(userId.ToString())}", content);

            return;
        }
        catch
        {
            throw;
        }
    }

    public string GetPhoneNumber(string input) => CloudGeography.PhoneNumbers.Get(input).Number;

    public async Task PasswordLogin(string email, string password, bool keepMeSignedIn)
    {
        MultipartFormDataContent form = new()
        {
            { new StringContent(email), "email" },
            { new StringContent(password), "password" },
            { new StringContent(keepMeSignedIn.ToString()), "keepMeSignedIn" }
        };

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Login/PasswordSignIn", form);

        if (!message.IsSuccessStatusCode)
            throw new Exception("Invalid email or password");
    }

    public async Task<User> PasswordRegistration(string email, string password, string firstName, string lastName)
    {
        MultipartFormDataContent form = new()
        {
            { new StringContent(email), "email" },
            { new StringContent(password), "password" },
            { new StringContent(firstName), "firstName" },
            { new StringContent(lastName),  "lastName" },
        };

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Login/PasswordRegistration", form);

        if (!message.IsSuccessStatusCode)
            throw new Exception("Invalid email or password");

        return (await message.Content.ReadFromJsonAsync<User>())!;
    }
}