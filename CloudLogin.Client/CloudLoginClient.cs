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
    public string AccountRoute = "CloudLogin/Account";
    public string SecurityRoute = "CloudLogin/Security";
    public string? RedirectUri { get; set; }
    public List<CloudLoginLink>? FooterLinks { get; set; }

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
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

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
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

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

    public async Task<List<CloudLoginProviderDefinition>> GetProviders()
    {
        HttpResponseMessage response = await HttpServer.GetAsync("api/providers");

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to get providers. Status code: {response.StatusCode}");

        string responseContent = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrEmpty(responseContent))
        {
            // Handle empty response
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CloudLoginProviderDefinition>>(responseContent, CloudLoginSerialization.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("CloudLogin returned an invalid provider response.", ex);
        }
    }

    public bool UsingDatabase { get; set; } = true;

    private static CloudGeographyClient? _cloudGepgraphy;
    public CloudGeographyClient CloudGeography => _cloudGepgraphy ??= new CloudGeographyClient();

    //Misc
    public CloudLoginInputFormat GetInputFormat(string input)
    {
        if (string.IsNullOrEmpty(input))
            return CloudLoginInputFormat.Other;

        if (IsInputValidEmailAddress(input))
            return CloudLoginInputFormat.EmailAddress;

        if (IsInputValidPhoneNumber(input))
            return CloudLoginInputFormat.PhoneNumber;

        return CloudLoginInputFormat.Other;
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
    public async Task<List<CloudUser>?> GetAllUsers()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetAllUsers");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            List<CloudUser>? selectedUser = await message.Content.ReadFromJsonAsync<List<CloudUser>?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }

    public async Task<List<CloudUser>> GetTestUsers()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetTestUsers");

            if (message.StatusCode == HttpStatusCode.NoContent) return [];

            List<CloudUser>? users = await message.Content.ReadFromJsonAsync<List<CloudUser>>(CloudLoginSerialization.Options);

            return users ?? [];
        }
        catch
        {
            throw;
        }
    }

    public async Task<CloudUser?> GetUserById(Guid userId)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserById?id={HttpUtility.UrlEncode(userId.ToString())}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }
    }
    public async Task<List<CloudUser>?> GetUsersByDisplayName(string displayName)
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            List<CloudUser>? selectedUsers = await message.Content.ReadFromJsonAsync<List<CloudUser>?>(CloudLoginSerialization.Options);

            if (selectedUsers == null) return null;

            return selectedUsers;
        }
        catch
        {
            throw;
        }

    }
    public async Task<CloudUser?> GetUserByDisplayName(string displayName)
    {
        if (!UsingDatabase)
            return null;

        HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

        if (message.StatusCode == HttpStatusCode.NoContent) return null;

        CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser?>(CloudLoginSerialization.Options);

        if (selectedUser == null) return null;

        return selectedUser;
    }
    public async Task<CloudUser?> GetUserByInput(string input)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByInput?input={HttpUtility.UrlEncode(input)}");

            if (message.StatusCode == HttpStatusCode.NoContent) return null;

            CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<CloudUser?> GetUserByEmailAddress(string email)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByEmailAdress?email={HttpUtility.UrlEncode(email)}");

            if (message.StatusCode == HttpStatusCode.NoContent || message.StatusCode == HttpStatusCode.NotFound || message.StatusCode == HttpStatusCode.InternalServerError) return null;

            CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }
    public async Task<CloudUser?> GetUserByPhoneNumber(string number)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserByPhoneNumber?number={HttpUtility.UrlEncode(number)}");

            if (message.StatusCode == HttpStatusCode.NoContent || message.StatusCode == HttpStatusCode.NotFound || message.StatusCode == HttpStatusCode.InternalServerError) return null;

            CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser?>(CloudLoginSerialization.Options);

            if (selectedUser == null) return null;

            return selectedUser;
        }
        catch
        {
            throw;
        }

    }

    //Request based functions
    public async Task<CloudUser?> GetUserByRequestId(Guid requestId)
    {
        if (!UsingDatabase)
            return null;

        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Request/GetUserByRequestId?requestId={HttpUtility.UrlEncode(requestId.ToString())}");

            if (message.IsSuccessStatusCode)
                return await message.Content.ReadFromJsonAsync<CloudUser?>(CloudLoginSerialization.Options);

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

        message.EnsureSuccessStatusCode();
        return await message.Content.ReadFromJsonAsync<Guid>(CloudLoginSerialization.Options);
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
    public async Task UpdateUser(CloudUser user)
    {
        if (!UsingDatabase)
            return;

        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync($"{UserRoute}/Update", content);
    }
    public async Task CreateUser(CloudUser user)
    {
        if (!UsingDatabase)
            return;

        HttpContent content = JsonContent.Create(user);

        await HttpServer.PostAsync($"{UserRoute}/Create", content);
    }
    public async Task<string> UploadProfilePicture(Guid userId, byte[] content, string contentType)
    {
        ByteArrayContent body = new(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        HttpResponseMessage message = await HttpServer.PostAsync(
            $"{UserRoute}/UploadProfilePicture?userId={userId}&contentType={HttpUtility.UrlEncode(contentType)}", body);

        message.EnsureSuccessStatusCode();

        return await message.Content.ReadAsStringAsync();
    }
    public async Task DeleteUser(Guid userId)
    {
        if (!UsingDatabase)
            return;

        await HttpServer.DeleteAsync($"{UserRoute}/Delete?userId={userId}");
    }
    public async Task<CloudUser?> CurrentUser()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/CurrentUser");

            if (message.StatusCode == HttpStatusCode.NoContent)
                return null;

            return await message.Content.ReadFromJsonAsync<CloudUser>(CloudLoginSerialization.Options);
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
    public async Task AddUserInput(Guid userId, CloudLoginInput Input)
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
    public async Task<bool> PasswordLogin(CloudLoginPasswordLoginRequest request)
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
            CloudUser? currentUser = await CurrentUser();

            if (currentUser != null && currentUser.ID != Guid.Empty)
                return true;

            return false;
        }

        return true;
    }

    public async Task<bool> TestLogin(Guid userId, bool keepMeSignedIn = false)
    {
        if (userId == Guid.Empty)
            return false;

        MultipartFormDataContent form = new()
        {
            { new StringContent(userId.ToString()), "userId" },
            { new StringContent(keepMeSignedIn.ToString()), "keepMeSignedIn" }
        };

        HttpResponseMessage message = await HttpServer.PostAsync("CloudLogin/Login/TestSignIn", form);
        return message.IsSuccessStatusCode;
    }

    public async Task<string> CompleteLoginRedirect(string? referer = null, bool isMobileApp = false)
    {
        List<string> parameters = [$"isMobileApp={isMobileApp.ToString().ToLowerInvariant()}"];
        if (!string.IsNullOrWhiteSpace(referer))
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

        HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Login/Complete?{string.Join("&", parameters)}");
        message.EnsureSuccessStatusCode();

        string target = (await message.Content.ReadAsStringAsync()).Trim();

        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("CloudLogin returned an empty redirect target.");

        return target;
    }

    public async Task<CloudUser> PasswordRegistration(CloudLoginPasswordRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MultipartFormDataContent form = new()
        {
            { new StringContent(request.Input), "input" },
            { new StringContent(request.InputFormat.ToString()), "inputFormat" },
            { new StringContent(request.Password ?? string.Empty), "password" },
            { new StringContent(request.FirstName), "firstName" },
            { new StringContent(request.LastName), "lastName" },
            { new StringContent(request.DisplayName), "displayName" }
        };

        HttpResponseMessage message = await HttpServer.PostAsync($"CloudLogin/Login/PasswordRegistration", form);

        if (!message.IsSuccessStatusCode)
        {
            // The status code is carried too: a rejected registration answers with a reason in the
            // body, but a rate-limited one (429) answers with nothing at all, and a bare "failed"
            // leaves no way to tell the two apart.
            string reason = (await message.Content.ReadAsStringAsync()).Trim();

            throw new Exception(reason.Length > 0
                ? $"Password registration failed ({(int)message.StatusCode}): {reason}"
                : $"Password registration failed ({(int)message.StatusCode} {message.ReasonPhrase}).");
        }

        return (await message.Content.ReadFromJsonAsync<CloudUser>(CloudLoginSerialization.Options))!;
    }

    public async Task<CloudUser> CodeRegistration(CloudLoginCodeRegistrationRequest request)
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
        return (await message.Content.ReadFromJsonAsync<CloudUser>(CloudLoginSerialization.Options))!;
    }

    // ── Admin methods ──────────────────────────────────────────────────

    public async Task<int> GetUserCount()
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"{UserRoute}/GetUserCount");

        if (!message.IsSuccessStatusCode)
            return 0;

        return await message.Content.ReadFromJsonAsync<int>(CloudLoginSerialization.Options);
    }

    public async Task SetUserLocked(Guid userId, bool locked)
    {
        HttpResponseMessage message = await HttpServer.PostAsync(
            $"{UserRoute}/Admin/SetLocked?userId={userId}&locked={locked}", null);

        if (!message.IsSuccessStatusCode)
            throw new Exception($"SetUserLocked failed: {message.StatusCode}");
    }

    public async Task AdminResetPassword(Guid userId, string newPassword)
    {
        HttpContent content = JsonContent.Create(newPassword);
        HttpResponseMessage message = await HttpServer.PostAsync($"{UserRoute}/Admin/ResetPassword?userId={userId}", content);

        if (!message.IsSuccessStatusCode)
            throw new Exception($"AdminResetPassword failed: {await message.Content.ReadAsStringAsync()}");
    }

    public async Task SetGlobalAdmin(Guid userId, bool isAdmin)
    {
        HttpResponseMessage message = await HttpServer.PostAsync($"{UserRoute}/Admin/SetGlobalAdmin?userId={userId}&isAdmin={isAdmin}", null);

        if (!message.IsSuccessStatusCode)
            throw new Exception($"SetGlobalAdmin failed: {message.StatusCode}");
    }

    // ── Account registry (workspaces only — commercial records live in the applications) ──────────

    public async Task<List<CloudWorkspace>> GetMyWorkspaces()
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"{AccountRoute}/Workspaces");

        if (!message.IsSuccessStatusCode)
            return [];

        List<CloudWorkspace>? workspaces = await message.Content.ReadFromJsonAsync<List<CloudWorkspace>>(CloudLoginSerialization.Options);

        return workspaces ?? [];
    }

    public async Task<CloudWorkspaceQuota> GetMyWorkspaceQuota()
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"{AccountRoute}/Workspaces/Quota");

        // Without a reachable quota the UI shouldn't invent an allowance: report none left rather
        // than offering a create button the server would refuse.
        if (!message.IsSuccessStatusCode)
            return new CloudWorkspaceQuota { Owned = 0, MaxOwned = 0, Total = 0, MaxTotal = 0 };

        return await message.Content.ReadFromJsonAsync<CloudWorkspaceQuota>(CloudLoginSerialization.Options)
            ?? new CloudWorkspaceQuota { Owned = 0, MaxOwned = 0, Total = 0, MaxTotal = 0 };
    }

    public async Task<CloudWorkspaceDetail?> GetWorkspaceDetail(Guid workspaceId)
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"{AccountRoute}/Workspaces/{workspaceId}/Detail");

        if (!message.IsSuccessStatusCode)
            return null;

        return await message.Content.ReadFromJsonAsync<CloudWorkspaceDetail>(CloudLoginSerialization.Options);
    }

    public async Task<CloudWorkspace> CreateWorkspace(string name)
    {
        HttpContent content = JsonContent.Create(new CloudLoginCreateWorkspaceRequest(name), options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{AccountRoute}/Workspaces", content);

        if (!message.IsSuccessStatusCode)
            throw await AccountFailure(message, "We couldn't create that workspace.");

        return (await message.Content.ReadFromJsonAsync<CloudWorkspace>(CloudLoginSerialization.Options))!;
    }

    public async Task<CloudWorkspaceInvitation> InviteToWorkspace(Guid workspaceId, string recipient, IReadOnlyList<string>? roles = null)
    {
        HttpContent content = JsonContent.Create(new CloudLoginInviteToWorkspaceRequest(recipient, roles), options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{AccountRoute}/Workspaces/{workspaceId}/Invite", content);

        if (!message.IsSuccessStatusCode)
            throw await AccountFailure(message, "We couldn't send that invitation.");

        return (await message.Content.ReadFromJsonAsync<CloudWorkspaceInvitation>(CloudLoginSerialization.Options))!;
    }

    public async Task<CloudWorkspace> UpdateWorkspace(CloudWorkspace workspace)
    {
        HttpContent content = JsonContent.Create(workspace, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PutAsync($"{AccountRoute}/Workspaces/{workspace.ID}", content);

        if (!message.IsSuccessStatusCode)
            throw await AccountFailure(message, "We couldn't save those changes.");

        return (await message.Content.ReadFromJsonAsync<CloudWorkspace>(CloudLoginSerialization.Options))!;
    }

    public async Task DeleteWorkspace(Guid workspaceId)
    {
        HttpResponseMessage message = await HttpServer.DeleteAsync($"{AccountRoute}/Workspaces/{workspaceId}");

        if (!message.IsSuccessStatusCode)
            throw await AccountFailure(message, "We couldn't delete that workspace.");
    }

    /// <summary>
    /// Turns a failed account call into an exception the account UI can show as-is. The account
    /// endpoints answer refusals the user can act on — a reached limit, a subscription that still
    /// blocks deletion — with a ProblemDetails <c>detail</c>, so that sentence is preferred over a
    /// status code. Anything else falls back to <paramref name="fallback"/> and never leaks a
    /// server-side message of unknown shape into the page.
    /// </summary>
    private static async Task<Exception> AccountFailure(HttpResponseMessage message, string fallback)
    {
        try
        {
            if (message.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                using JsonDocument document = JsonDocument.Parse(await message.Content.ReadAsStringAsync());

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("detail", out JsonElement detail)
                    && detail.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(detail.GetString()))
                    return new InvalidOperationException(detail.GetString());
            }
        }
        catch (JsonException) { }

        return message.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException("Sign in again to continue."),
            HttpStatusCode.Forbidden => new UnauthorizedAccessException("You don't have permission to do this."),
            HttpStatusCode.NotFound => new KeyNotFoundException(fallback),
            HttpStatusCode.NotImplemented => new NotSupportedException("This host doesn't support that yet."),
            _ => new InvalidOperationException(fallback)
        };
    }

    public async Task<CloudWorkspace?> GetWorkspaceById(Guid workspaceId)
    {
        // Service-to-service lookup, gated by the ServiceKey scheme — not reachable from the browser client.
        HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Service/Workspaces/{workspaceId}");

        if (!message.IsSuccessStatusCode)
            return null;

        return await message.Content.ReadFromJsonAsync<CloudWorkspace?>(CloudLoginSerialization.Options);
    }

    public async Task<List<CloudWorkspace>> GetAllWorkspaces()
    {
        // Service-to-service lookup, gated by the ServiceKey scheme — not reachable from the browser client.
        HttpResponseMessage message = await HttpServer.GetAsync("CloudLogin/Service/Workspaces");

        if (!message.IsSuccessStatusCode)
            return [];

        return await message.Content.ReadFromJsonAsync<List<CloudWorkspace>>(CloudLoginSerialization.Options) ?? [];
    }

    public async Task<List<CloudWorkspaceMember>> GetWorkspaceMembers(Guid workspaceId)
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"CloudLogin/Service/Workspaces/{workspaceId}/Members");

        if (!message.IsSuccessStatusCode)
            return [];

        return await message.Content.ReadFromJsonAsync<List<CloudWorkspaceMember>>(CloudLoginSerialization.Options) ?? [];
    }


    // ── Security ─────────────────────────────────────────────────────────────
    // The server derives the acting user from the session cookie, so none of these
    // calls carries a user id.

    public async Task<CloudLoginSecurityOverview> GetSecurityOverview()
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"{SecurityRoute}/Overview");

        if (!message.IsSuccessStatusCode)
            return new CloudLoginSecurityOverview();

        return await message.Content.ReadFromJsonAsync<CloudLoginSecurityOverview>(CloudLoginSerialization.Options) ?? new CloudLoginSecurityOverview();
    }

    public async Task<List<CloudLoginHistoryEntry>> GetMyLoginHistory()
    {
        HttpResponseMessage message = await HttpServer.GetAsync($"{SecurityRoute}/LoginHistory");

        if (!message.IsSuccessStatusCode)
            return [];

        return await message.Content.ReadFromJsonAsync<List<CloudLoginHistoryEntry>>(CloudLoginSerialization.Options) ?? [];
    }

    public async Task ChangeMyPassword(CloudLoginChangePasswordRequest request)
    {
        HttpContent content = JsonContent.Create(request, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Password", content);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));
    }

    public async Task DisconnectProvider(string providerCode, string input)
    {
        HttpContent content = JsonContent.Create(new { providerCode, input }, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/DisconnectProvider", content);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));
    }

    public async Task<CloudLoginAuthenticatorEnrollment> BeginAuthenticatorEnrollment()
    {
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Authenticator/Begin", null);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));

        return (await message.Content.ReadFromJsonAsync<CloudLoginAuthenticatorEnrollment>(CloudLoginSerialization.Options))!;
    }

    public async Task<bool> ConfirmAuthenticatorEnrollment(string code)
    {
        HttpContent content = JsonContent.Create(new { code }, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Authenticator/Confirm", content);

        if (!message.IsSuccessStatusCode)
            return false;

        return await message.Content.ReadFromJsonAsync<bool>(CloudLoginSerialization.Options);
    }

    public async Task DisableAuthenticator()
    {
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Authenticator/Disable", null);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));
    }

    public async Task<string> BeginPasskeyRegistration()
    {
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Passkeys/Begin", null);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));

        return await message.Content.ReadAsStringAsync();
    }

    public async Task<CloudLoginPasskeySummary> CompletePasskeyRegistration(string optionsJson, string attestationJson, string? name)
    {
        HttpContent content = JsonContent.Create(new { optionsJson, attestationJson, name }, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Passkeys/Complete", content);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));

        return (await message.Content.ReadFromJsonAsync<CloudLoginPasskeySummary>(CloudLoginSerialization.Options))!;
    }

    public async Task RemovePasskey(string credentialId)
    {
        HttpContent content = JsonContent.Create(new { credentialId }, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Passkeys/Remove", content);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));
    }

    public async Task RenamePasskey(string credentialId, string name)
    {
        HttpContent content = JsonContent.Create(new { credentialId, name }, options: CloudLoginSerialization.Options);
        HttpResponseMessage message = await HttpServer.PostAsync($"{SecurityRoute}/Passkeys/Rename", content);

        if (!message.IsSuccessStatusCode)
            throw new Exception(await ReadProblem(message));
    }

    /// <summary>
    /// Surfaces the server's reason for rejecting a security change, so the account page can
    /// show "Your current password is incorrect" rather than a bare status code.
    /// </summary>
    private static async Task<string> ReadProblem(HttpResponseMessage message)
    {
        string body = await message.Content.ReadAsStringAsync();

        return string.IsNullOrWhiteSpace(body) ? message.ReasonPhrase ?? "Request failed." : body;
    }
}
