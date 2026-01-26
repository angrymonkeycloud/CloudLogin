using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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

    // URL Generation methods for login flows
    /// <summary>
    /// Generates a login URL for web applications
    /// </summary>
    /// <param name="referer">The external website URL that referred to CloudLogin</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <returns>The complete login URL</returns>
    public string GetLoginUrl(string? referer = null, bool isMobileApp = false)
    {
        string baseUrl = LoginUrl.TrimEnd('/');
        referer ??= RedirectUri ?? "/";

        var parameters = new List<string>();

        if (!string.IsNullOrEmpty(referer))
            parameters.Add($"referer={referer}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/{queryString}";
    }

    /// <summary>
    /// Generates a login URL for external provider authentication
    /// </summary>
    /// <param name="providerCode">The provider code (e.g., "google", "microsoft")</param>
    /// <param name="referer">The external website URL that referred to CloudLogin (legacy parameter name)</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <param name="keepMeSignedIn">Whether to maintain persistent session</param>
    /// <param name="finalReferer">The external website URL that referred to CloudLogin</param>
    /// <returns>The complete provider login URL</returns>
    public string GetProviderLoginUrl(string providerCode, string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false)
    {
        if (string.IsNullOrEmpty(providerCode))
            throw new ArgumentException("Provider code cannot be null or empty", nameof(providerCode));

        string baseUrl = LoginUrl.TrimEnd('/');
        referer ??= RedirectUri ?? "/";

        var parameters = new List<string>();

        if (!string.IsNullOrEmpty(referer))
            parameters.Add($"referer={referer}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        if (keepMeSignedIn)
            parameters.Add("keepMeSignedIn=true");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/cloudlogin/login/{providerCode.ToLowerInvariant()}{queryString}";
    }

    /// <summary>
    /// Generates a custom login URL with additional parameters
    /// </summary>
    /// <param name="referer">The external website URL that referred to CloudLogin (legacy parameter name)</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <param name="keepMeSignedIn">Whether to maintain persistent session</param>
    /// <param name="userHint">Optional user hint (email/phone)</param>
    /// <param name="finalReferer">The external website URL that referred to CloudLogin</param>
    /// <returns>The complete custom login URL</returns>
    public string GetCustomLoginUrl(string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false, string? userHint = null)
    {
        string baseUrl = LoginUrl.TrimEnd('/');
        referer ??= RedirectUri ?? "/";

        var parameters = new List<string>();

        if (!string.IsNullOrEmpty(referer))
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        if (keepMeSignedIn)
            parameters.Add("keepMeSignedIn=true");

        if (!string.IsNullOrEmpty(userHint))
            parameters.Add($"input={HttpUtility.UrlEncode(userHint)}");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/cloudlogin/login{queryString}";
    }

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
    public async Task<List<UserModel>?> GetAllUsers()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetAllUsers");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            List<UserModel>? selectedUser = await message.Content.ReadFromJsonAsync<List<UserModel>?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<UserModel?> GetUserById(Guid userId)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserById?id={HttpUtility.UrlEncode(userId.ToString())}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            UserModel? selectedUser = await message.Content.ReadFromJsonAsync<UserModel?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }
    }
    public async Task<List<UserModel>?> GetUsersByDisplayName(string displayName)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            List<UserModel>? selectedUsers = await message.Content.ReadFromJsonAsync<List<UserModel>?>(CloudLoginSerialization.Options);

            if (selectedUsers == null) return null;

            return selectedUsers;
        }
        catch
        {
            throw;
        }

    }
    public async Task<UserModel?> GetUserByDisplayName(string displayName)
    {
        if (!UsingDatabase)
            return null;

        HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

        if (message.StatusCode == HttpStatusCode.NoContent) return null;

        UserModel? selectedUser = await message.Content.ReadFromJsonAsync<UserModel?>(CloudLoginSerialization.Options);

        if (selectedUser == null) return null;

        return selectedUser;
    }
    public async Task<UserModel?> GetUserByInput(string input)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByInput?input={HttpUtility.UrlEncode(input)}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            UserModel? selectedUser = await message.Content.ReadFromJsonAsync<UserModel?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<UserModel?> GetUserByEmailAddress(string email)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByEmailAdress?email={HttpUtility.UrlEncode(email)}");

            if (message.StatusCode == HttpStatusCode.NoContent || message.StatusCode == HttpStatusCode.InternalServerError) return null;

            UserModel? selectedUser = await message.Content.ReadFromJsonAsync<UserModel?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<UserModel?> GetUserByPhoneNumber(string number)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByPhoneNumber?number={HttpUtility.UrlEncode(number)}");

            if (message.StatusCode == HttpStatusCode.NoContent || message.StatusCode == HttpStatusCode.InternalServerError) return null;

            UserModel? selectedUser = await message.Content.ReadFromJsonAsync<UserModel?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }

    //Request based functions
    public async Task<UserModel?> GetUserByRequestId(Guid requestId)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Request/GetUserByRequestId?requestId={HttpUtility.UrlEncode(requestId.ToString())}");

            if (message.IsSuccessStatusCode)
                return await message.Content.ReadFromJsonAsync<UserModel?>(CloudLoginSerialization.Options);

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
    public async Task UpdateUser(UserModel user)
    {
        if (!UsingDatabase)
            return;

        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync($"{UserRoute}/Update", content);
    }
    public async Task CreateUser(UserModel user)
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
    public async Task<UserModel?> CurrentUser()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/CurrentUser");

            if (message.StatusCode == HttpStatusCode.NoContent)
                return null;

            return await message.Content.ReadFromJsonAsync<UserModel>(CloudLoginSerialization.Options);
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

    public bool IsValidPassword(string password)
    {
        // Regular expression to check for at least one letter, one capital letter, and minimum length of 6
        string pattern = @"^(?=.*[a-z])(?=.*[A-Z]).{6,}$";
        return Regex.IsMatch(password, pattern);
    }

    // Model-based methods
    public async Task<bool> PasswordLogin(PasswordLoginRequest request)
    {
        MultipartFormDataContent form = new()
        {
            { new StringContent(request.Email), "email" },
            { new StringContent(request.Password), "password" },
            { new StringContent(request.KeepMeSignedIn.ToString()), "keepMeSignedIn" }
        };

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Login/PasswordSignIn", form);

        if (!message.IsSuccessStatusCode)
        {
            UserModel? currentUser = await CurrentUser();

            if (currentUser != null && currentUser.ID != Guid.Empty)
                return true;

            return false;
        }

        return true;
    }

    public async Task<UserModel> PasswordRegistration(PasswordRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MultipartFormDataContent form = new()
        {
            { new StringContent(request.Input), "input" },
            { new StringContent(request.InputFormat.ToString()), "inputFormat" },
            { new StringContent(request.Password), "password" },
            { new StringContent(request.FirstName), "firstName" },
            { new StringContent(request.LastName), "lastName" },
            { new StringContent(request.DisplayName), "displayName" }
        };

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Login/PasswordRegistration", form);

        if (!message.IsSuccessStatusCode)
            throw new Exception("Password registration failed");

        return (await message.Content.ReadFromJsonAsync<UserModel>(CloudLoginSerialization.Options))!;
    }

    public async Task<UserModel> CodeRegistration(CodeRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MultipartFormDataContent form = new()
        {
            { new StringContent(request.Input), "input" },
            { new StringContent(request.InputFormat.ToString()), "inputFormat" },
            { new StringContent(request.FirstName), "firstName" },
            { new StringContent(request.LastName), "lastName" },
            { new StringContent(request.DisplayName), "displayName" }
        };

        HttpResponseMessage message = await HttpServer.PostAsync("CloudLogin/Login/CodeRegistration", form);
        if (!message.IsSuccessStatusCode) throw new Exception("Code registration failed");
        return (await message.Content.ReadFromJsonAsync<UserModel>(CloudLoginSerialization.Options))!;
    }
}