using AngryMonkey.CloudLogin;
using CloudLogin.Shared;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Maui.Storage;
using CloudLogin.Shared.CloudLoginServices;
using CloudLogin.Shared.NavigationServices;

namespace CloudLogin.AppService;

public static class MobileAuthCallback
{
    public static event Action<string>? RequestIdReceived;
    public static void Raise(string requestId) => RequestIdReceived?.Invoke(requestId);
}

public class CloudLoginAppService : CloudLoginBaseService, IDisposable
{
    public const string CallbackUrl = "mahloole://auth/callback";

    // Secure storage keys
    private const string SecureUserIdKey = "coverbox_secure_user_id";
    private const string SecureRequestIdKey = "coverbox_secure_request_id";

    // Preferences keys
    private const string UserDataKey = "coverbox_user_data";
    private const string PostLoginRouteKey = "coverbox_post_login_route";
    private const string LastLoginTimestampKey = "coverbox_last_login_timestamp";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private INavigationService? _nav; // Lazy-loaded - set when Blazor initializes
    private bool _disposed;
    private bool _initialized;
    private static CloudLoginBaseService? _activeSubscriber;

    public CloudLoginAppService()
       : base()
    {
        // Lightweight constructor - just event subscriptions
        UserChanged += OnUserChangedInternal;

        try
        {
            if (_activeSubscriber is not null)
            {
                MobileAuthCallback.RequestIdReceived -= OnRequestIdReceived;
            }

            MobileAuthCallback.RequestIdReceived += OnRequestIdReceived;
            _activeSubscriber = this;
        }
        catch { }
    }

    
    /// <summary>
    /// Initialize the account service and restore any saved session.
    /// Call this once during app startup from App.xaml.cs
    /// Does NOT require NavigationService to be set.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;

        // Handle post-login route restoration
        try
        {
            if (Preferences.Default.ContainsKey(PostLoginRouteKey))
            {
                string? route = Preferences.Default.Get<string>(PostLoginRouteKey, null);
                Preferences.Default.Remove(PostLoginRouteKey);

                if (!string.IsNullOrWhiteSpace(route))
                {
                    var normalized = NormalizeToBaseRelative(route);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Failed to restore post-login route: {ex.Message}");
        }

        // Restore user session - doesn't need navigation
        await RestoreUserSessionAsync();
    }

    private async Task RestoreUserSessionAsync()
    {
        try
        {
            // Get user ID from secure storage
            string? userIdStr = await SecureStorage.Default.GetAsync(SecureUserIdKey);

            if (string.IsNullOrWhiteSpace(userIdStr))
            {
                Debug.WriteLine("[AccountService] No stored session found");
                return;
            }

            if (!Guid.TryParse(userIdStr, out Guid userId))
            {
                Debug.WriteLine("[AccountService] Invalid stored user ID");
                await ClearStoredSessionAsync();
                return;
            }

            // Check session expiration (30 days)
            if (Preferences.Default.ContainsKey(LastLoginTimestampKey))
            {
                long timestamp = Preferences.Default.Get(LastLoginTimestampKey, 0L);
                DateTime lastLogin = DateTime.FromBinary(timestamp);

                if (DateTime.UtcNow - lastLogin > TimeSpan.FromDays(30))
                {
                    Debug.WriteLine("[AccountService] Session expired (30 days)");
                    await ClearStoredSessionAsync();
                    return;
                }
            }

            // Load cached user data
            if (Preferences.Default.ContainsKey(UserDataKey))
            {
                string? json = Preferences.Default.Get(UserDataKey, string.Empty);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    User = JsonSerializer.Deserialize<User>(json, JsonOptions);

                    if (User != null)
                    {
                        Debug.WriteLine($"[AccountService] Restored session: {User.DisplayName} ({User.ID})");

                        return;
                    }
                }
            }

            // Try to restore from server if we have request ID
            string? storedRequestId = await SecureStorage.Default.GetAsync(SecureRequestIdKey);

            if (!string.IsNullOrWhiteSpace(storedRequestId))
            {
                Debug.WriteLine("[AccountService] Restoring session from server");
                RequestId = storedRequestId;
                await FetchUser();
            }
            else
            {
                Debug.WriteLine("[AccountService] No request ID, clearing stale session");
                await ClearStoredSessionAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Failed to restore session: {ex.Message}");
            await ClearStoredSessionAsync();
        }
    }

    private async void OnUserChangedInternal(User? user)
    {
        if (user != null)
            await PersistUserSessionAsync(user);
        else
            await ClearStoredSessionAsync();
    }

    private async Task PersistUserSessionAsync(User user)
    {
        try
        {
            await SecureStorage.Default.SetAsync(SecureUserIdKey, user.ID.ToString());

            if (!string.IsNullOrWhiteSpace(RequestId))
            {
                await SecureStorage.Default.SetAsync(SecureRequestIdKey, RequestId);
            }

            string json = JsonSerializer.Serialize(user, JsonOptions);
            Preferences.Default.Set(UserDataKey, json);
            Preferences.Default.Set(LastLoginTimestampKey, DateTime.UtcNow.ToBinary());

            Debug.WriteLine($"[AccountService] Persisted session: {user.DisplayName} ({user.ID})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Failed to persist session: {ex.Message}");
        }
    }

    private async Task ClearStoredSessionAsync()
    {
        try
        {
            SecureStorage.Default.Remove(SecureUserIdKey);
            SecureStorage.Default.Remove(SecureRequestIdKey);
            Preferences.Default.Remove(UserDataKey);
            Preferences.Default.Remove(LastLoginTimestampKey);

            Debug.WriteLine("[AccountService] Cleared stored session");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Failed to clear session: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public override async Task Login()
    {
        if (_nav == null)
        {
            Debug.WriteLine("[AccountService] Login failed: NavigationService not initialized");
            return;
        }

        string current = _nav.CurrentUri;
        string relative = NormalizeToBaseRelative(current);
        await _nav.NavigateToAsync($"/signin?returnUrl={Uri.EscapeDataString(relative)}");
    }

    public override async Task BeginLoginAsync(string? returnUrl)
    {
        if (User != null)
            return;

        if (_nav == null)
        {
            Debug.WriteLine("[AccountService] BeginLogin failed: NavigationService not initialized");
            return;
        }

        string relative = NormalizeToBaseRelative(returnUrl ?? _nav.CurrentUri);
        string callbackWithReturn = $"{CallbackUrl}?return={Uri.EscapeDataString(relative)}";
        string startUrl = $"{LoginBaseUrl}?referer={Uri.EscapeDataString(callbackWithReturn)}";

        try
        {
            WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(startUrl),
                new Uri(callbackWithReturn));

            if (result?.Properties?.TryGetValue("requestId", out string? requestId) == true
                && !string.IsNullOrWhiteSpace(requestId))
            {
                RequestId = requestId;
                return;
            }
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("[AccountService] Login cancelled");
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Login failed: {ex.Message}");
        }
    }

    public override async Task Logout()
    {
        await ClearStoredSessionAsync();
        await base.Logout();

        if (_nav != null)
        {
            await ForceReloadTo("/");
        }
    }

    public override async Task<string> ProfileUrl()
    {
        return await Task.FromResult($"{LoginBaseUrl}/Account?referer={Uri.EscapeDataString(CallbackUrl)}");
    }

    private async void OnRequestIdReceived(string requestId)
    {
        RequestId = requestId;
    }

    private async Task ForceReloadTo(string target)
    {
        if (_nav == null)
        {
            Debug.WriteLine("[AccountService] ForceReloadTo failed: NavigationService not initialized");
            return;
        }

        try
        {
            string normalized = NormalizeToBaseRelative(target);
            await _nav.NavigateToAsync(normalized, forceReload: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Navigation failed: {ex.Message}");
        }
    }

    private static string NormalizeToBaseRelative(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "/";

        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            var path = abs.AbsolutePath;
            if (string.IsNullOrWhiteSpace(path)) path = "/";
            if (!path.StartsWith('/')) path = "/" + path;
            if (!string.IsNullOrEmpty(abs.Query)) path += abs.Query;
            return path;
        }

        return url.StartsWith('/') ? url : "/" + url;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            UserChanged -= OnUserChangedInternal;

            if (_activeSubscriber == this)
            {
                MobileAuthCallback.RequestIdReceived -= OnRequestIdReceived;
                _activeSubscriber = null;
            }
        }
        catch { }
        GC.SuppressFinalize(this);
    }


}
