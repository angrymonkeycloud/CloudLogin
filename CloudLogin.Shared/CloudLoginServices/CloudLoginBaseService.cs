using AngryMonkey.CloudLogin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloudLogin.Shared.CloudLoginServices;

public abstract class CloudLoginBaseService : ICloudLoginService
{
    protected const string LoginBaseUrl = "https://login2.coverbox.app";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event Action<UserModel?>? UserChanged;
    public event Action<string>? RequestIdChanged;

    private static string? _requestId;
    public string? RequestId
    {
        get => _requestId;
        protected set
        {
            _requestId = value;

            if (!string.IsNullOrEmpty(value))
                try { RequestIdChanged?.Invoke(value); } catch { }
        }
    }
    public void SetRequestId(string? requestId) => RequestId = requestId;

    private static UserModel? _user;
    public UserModel? User
    {
        get => _user;
        protected set
        {
            _user = value;
            RequestId = null;
        }
    }


    public abstract Task Login();
    public abstract Task BeginLoginAsync(string? returnUrl);
    public abstract Task<string> ProfileUrl();

    public virtual async Task Logout()
    {
        User = null;
        OnUserSignedOut();

        await Task.CompletedTask;
    }

    public async Task FetchUser()
    {
        if (string.IsNullOrEmpty(RequestId))
            return;

        try
        {
            // requestId expected to be a GUID; if not, call anyway (API will fail gracefully)
            using HttpClient http = new() { BaseAddress = new Uri(LoginBaseUrl) };
            HttpResponseMessage resp = await http.GetAsync($"/CloudLogin/Request/GetUserByRequestId?requestId={Uri.EscapeDataString(RequestId)}");

            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AccountService] Fetch user HTTP {(int)resp.StatusCode}");
                return;
            }

            User = await resp.Content.ReadFromJsonAsync<UserModel>(JsonOptions);
            UserChanged?.Invoke(_user);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Fetch user exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches user information by email address
    /// </summary>
    /// <param name="emailAddress">The email address to lookup</param>
    /// <returns>User object if found, null otherwise</returns>
    public async Task<UserModel?> FetchUserByEmail(string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return null;

        try
        {
            using HttpClient http = new() { BaseAddress = new Uri(LoginBaseUrl) };
            HttpResponseMessage resp = await http.GetAsync($"/CloudLogin/User/GetUserByEmailAddress?emailAddress={Uri.EscapeDataString(emailAddress)}");

            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AccountService] Fetch user by email HTTP {(int)resp.StatusCode}");
                return null;
            }

            UserModel? user = await resp.Content.ReadFromJsonAsync<UserModel>(JsonOptions);
            return user;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Fetch user by email exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Called after a user signs out. Clears all cached user data and permissions.
    /// </summary>
    protected void OnUserSignedOut()
    {
        User = null;
    }

    protected void RaiseUserChanged(UserModel? user) => UserChanged?.Invoke(user);


}
