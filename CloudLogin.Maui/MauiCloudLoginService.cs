using AngryMonkey.CloudLogin;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;
using AngryMonkey.CloudBlazor.App;

namespace AngryMonkey.CloudLogin;

/// <summary>What the system browser handed back to the app on its custom URL scheme.</summary>
public sealed record MauiAuthCallback(string? Handoff, string? State, string? Operation, string? Error);

public static class MobileAuthCallback
{
    public static event Action<MauiAuthCallback>? Received;

    private static MauiAuthCallback? _pending;
    private static readonly object _lock = new();

    public static void Raise(MauiAuthCallback callback)
    {
        lock (_lock)
        {
            if (Received is not null)
            {
                _pending = null;
                Received.Invoke(callback);
            }
            else
            {
                // No subscriber yet: keep it so it can be consumed once the service exists.
                _pending = callback;
                Debug.WriteLine("[MobileAuthCallback] Buffered a callback (no subscriber).");
            }
        }
    }

    /// <summary>Consumes a callback that arrived before a subscriber was attached, or null.</summary>
    public static MauiAuthCallback? ConsumePending()
    {
        lock (_lock)
        {
            MauiAuthCallback? callback = _pending;
            _pending = null;
            return callback;
        }
    }
}

/// <summary>
/// Native sign-in through the app's own backend. The app makes a PKCE verifier and state, the system browser signs in through the
/// backend, and the backend hands back a one-time handoff that only this app can redeem because only it holds the verifier. The verifier
/// is kept in secure storage until then, so a sign-in survives the app being stopped while the browser is open.
/// </summary>
public class MauiCloudLoginService : CloudLoginBaseService, IDisposable
{
    private static readonly TimeSpan PendingLoginLifetime = TimeSpan.FromMinutes(10);

    private readonly MauiCloudLoginOptions _options;
    private string CallbackUrl => _options.CallbackUrl;
    private string SecureUserIdKey => _options.StorageKey("secure.user-id");
    private string PendingLoginKey => _options.StorageKey("secure.pending-login");
    private string UserDataKey => _options.StorageKey("user-data");
    private string PostLoginRouteKey => _options.StorageKey("post-login-route");
    private string LastLoginTimestampKey => _options.StorageKey("last-login-timestamp");
    private string PendingAuthorityLogoutKey => _options.StorageKey("pending-authority-logout");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record PendingLogin(string State, string Verifier, DateTimeOffset StartedOn);

    private INavigationService? _nav; // Lazy-loaded - set when Blazor initializes
    private bool _disposed;
    private bool _initialized;
    private static CloudLoginBaseService? _activeSubscriber;
    private static readonly SemaphoreSlim LoginCompletionLock = new(1, 1);
    private readonly IReadOnlyList<IMauiCloudLoginNativeExchange> _exchanges;

    public MauiCloudLoginService(
        INavigationService navigationService,
        MauiCloudLoginOptions options,
        IEnumerable<IMauiCloudLoginNativeExchange>? exchanges = null) : base()
    {
        _nav = navigationService;
        _options = options;
        _exchanges = exchanges?.ToList() ?? [];

        // Lightweight constructor - just event subscriptions
        UserChanged += OnUserChangedInternal;

        try
        {
            if (_activeSubscriber is not null)
                MobileAuthCallback.Received -= OnCallbackReceived;

            MobileAuthCallback.Received += OnCallbackReceived;
            _activeSubscriber = this;

            // A callback may have arrived (the app was launched by it) before this service existed.
            if (MobileAuthCallback.ConsumePending() is { } pending)
            {
                Debug.WriteLine("[MauiCloudLoginService] Consuming a buffered callback.");
                OnCallbackReceived(pending);
            }
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
            string? userIdStr = await SecureStorage.Default.GetAsync(SecureUserIdKey);

            if (string.IsNullOrWhiteSpace(userIdStr))
            {
                Debug.WriteLine("[AccountService] No stored session found");
                return;
            }

            if (!Guid.TryParse(userIdStr, out Guid _))
            {
                Debug.WriteLine("[AccountService] Invalid stored user Id");
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

            // The cached profile is only a display copy; the backend's own cookie decides whether the session is still valid.
            if (Preferences.Default.ContainsKey(UserDataKey))
            {
                string? json = Preferences.Default.Get(UserDataKey, string.Empty);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    User = JsonSerializer.Deserialize<CloudUser>(json, JsonOptions);

                    if (User != null)
                    {
                        Debug.WriteLine($"[AccountService] Restored session: {User.DisplayName} ({User.Id})");
                        return;
                    }
                }
            }

            Debug.WriteLine("[AccountService] No cached profile, clearing stale session");
            await ClearStoredSessionAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Failed to restore session: {ex.Message}");
            await ClearStoredSessionAsync();
        }
    }

    private async void OnUserChangedInternal(CloudUser? user)
    {
        if (user != null)
            await PersistUserSessionAsync(user);
        else
            await ClearStoredSessionAsync();
    }

    private async Task PersistUserSessionAsync(CloudUser user)
    {
        try
        {
            await SecureStorage.Default.SetAsync(SecureUserIdKey, user.Id.ToString());

            string json = JsonSerializer.Serialize(user, JsonOptions);
            Preferences.Default.Set(UserDataKey, json);
            Preferences.Default.Set(LastLoginTimestampKey, DateTime.UtcNow.ToBinary());

            Debug.WriteLine($"[AccountService] Persisted session: {user.DisplayName} ({user.Id})");
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
        await _nav.NavigateToAsync($"{LocalLoginPagePath}?returnUrl={Uri.EscapeDataString(relative)}");
    }

    public override async Task BeginLoginAsync(string? returnUrl)
    {
        if (User != null)
            return;

        if (_exchanges.Count == 0)
            throw new InvalidOperationException(
                $"Register an {nameof(IMauiCloudLoginNativeExchange)} so the app can keep the session the sign-in returns.");

        // A cancelled/offline logout may have cleared the local app while leaving
        // the authority cookie intact. Finish that logout before allowing another
        // sign-in, otherwise the old account could be returned automatically.
        if (Preferences.Default.Get(PendingAuthorityLogoutKey, false) &&
            !await TryLogoutAuthorityAsync())
            return;

        if (_nav == null)
        {
            Debug.WriteLine("[AccountService] BeginLogin failed: NavigationService not initialized");
            return;
        }

        string relative = NormalizeToBaseRelative(returnUrl ?? _nav.CurrentUri);
        PendingLogin pending = new(CloudLoginPkce.CreateState(), CloudLoginPkce.CreateVerifier(), DateTimeOffset.UtcNow);

        await SecureStorage.Default.SetAsync(PendingLoginKey, JsonSerializer.Serialize(pending));
        try { Preferences.Default.Set(PostLoginRouteKey, relative); } catch { }

        string startUrl = $"{_options.ApplicationUrl}/auth/native/login"
            + $"?challenge={Uri.EscapeDataString(CloudLoginPkce.CreateChallenge(pending.Verifier))}"
            + $"&state={Uri.EscapeDataString(pending.State)}"
            + $"&redirect_uri={Uri.EscapeDataString(CallbackUrl)}";

        try
        {
            WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(new Uri(startUrl), new Uri(CallbackUrl));

            bool completed = await CompleteLoginAsync(new MauiAuthCallback(
                Property(result, "handoff"), Property(result, "state"), Property(result, "operation"), Property(result, "error")));

            if (completed)
                await NavigateAfterLoginAsync();
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("[AccountService] Login cancelled");
            SecureStorage.Default.Remove(PendingLoginKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Login failed: {ex.Message}");
        }
    }

    public override async Task Logout()
    {
        Preferences.Default.Set(PendingAuthorityLogoutKey, true);

        await TryLogoutAuthorityAsync();

        await ClearStoredSessionAsync();
        await base.Logout();

        if (_nav != null)
            await ForceReloadTo("/");
    }

    private async Task<bool> TryLogoutAuthorityAsync()
    {
        try
        {
            // The same browser session the sign-in used, so the authority's own cookie is cleared as well as this app's session.
            string state = CloudLoginPkce.CreateState();
            string logoutUrl = $"{_options.ApplicationUrl}/auth/native/logout"
                + $"?redirect_uri={Uri.EscapeDataString(CallbackUrl)}&state={Uri.EscapeDataString(state)}";

            WebAuthenticatorResult result = await WebAuthenticator.Default.AuthenticateAsync(new Uri(logoutUrl), new Uri(CallbackUrl));

            bool ended = string.Equals(Property(result, "operation"), "logout", StringComparison.Ordinal)
                && StateMatches(Property(result, "state"), state)
                && string.IsNullOrEmpty(Property(result, "error"));

            if (ended)
                Preferences.Default.Remove(PendingAuthorityLogoutKey);

            return ended;
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("[AccountService] Authority logout was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountService] Authority logout failed: {ex.Message}");
            return false;
        }
    }

    public override async Task<string> ProfileUrl() => await Task.FromResult($"{LoginBaseUrl}/Account");

    private async void OnCallbackReceived(MauiAuthCallback callback)
    {
        Debug.WriteLine("[MauiCloudLoginService] A sign-in callback arrived.");

        try
        {
            if (await CompleteLoginAsync(callback))
                await NavigateAfterLoginAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MauiCloudLoginService] Completing the sign-in failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Redeems a handoff, but only for the sign-in this app started: its <c>state</c> must match the one it kept, and that is checked
    /// before anything is spent, so a stray or hostile callback can neither complete a login nor cancel the one in progress.
    /// </summary>
    private async Task<bool> CompleteLoginAsync(MauiAuthCallback callback)
    {
        if (string.Equals(callback.Operation, "logout", StringComparison.Ordinal))
            return false;

        await LoginCompletionLock.WaitAsync();
        try
        {
            PendingLogin? pending = await ReadPendingLoginAsync();

            if (pending is null || !StateMatches(callback.State, pending.State))
            {
                Debug.WriteLine("[AccountService] A callback arrived that no sign-in of this app is waiting for.");
                return false;
            }

            SecureStorage.Default.Remove(PendingLoginKey);

            if (!string.IsNullOrEmpty(callback.Error) || string.IsNullOrWhiteSpace(callback.Handoff))
            {
                Debug.WriteLine($"[AccountService] The sign-in ended without a handoff ({callback.Error}).");
                return false;
            }

            CloudUser? user = null;

            foreach (IMauiCloudLoginNativeExchange exchange in _exchanges)
            {
                user = await exchange.ExchangeAsync(callback.Handoff, pending.Verifier);

                if (user is not null)
                    break;
            }

            if (user is null)
                throw new InvalidOperationException("The native application could not establish its authenticated API session.");

            User = user;
            RaiseUserChanged(User);
            return true;
        }
        finally
        {
            LoginCompletionLock.Release();
        }
    }

    private async Task<PendingLogin?> ReadPendingLoginAsync()
    {
        try
        {
            string? stored = await SecureStorage.Default.GetAsync(PendingLoginKey);

            if (string.IsNullOrWhiteSpace(stored))
                return null;

            PendingLogin? pending = JsonSerializer.Deserialize<PendingLogin>(stored);

            if (pending is null || DateTimeOffset.UtcNow - pending.StartedOn > PendingLoginLifetime)
            {
                SecureStorage.Default.Remove(PendingLoginKey);
                return null;
            }

            return pending;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            SecureStorage.Default.Remove(PendingLoginKey);
            return null;
        }
    }

    private async Task NavigateAfterLoginAsync()
    {
        if (User is null)
            return;

        string target = "/";

        try
        {
            if (!string.IsNullOrWhiteSpace(Preferences.Default.Get<string>(PostLoginRouteKey, null)))
            {
                target = NormalizeToBaseRelative(Preferences.Default.Get<string>(PostLoginRouteKey, null));
                Preferences.Default.Remove(PostLoginRouteKey);
            }
        }
        catch { }

        await ForceReloadTo(target);
    }

    private static string? Property(WebAuthenticatorResult? result, string name) =>
        result?.Properties is not null && result.Properties.TryGetValue(name, out string? value) ? value : null;

    private static bool StateMatches(string? received, string expected) =>
        !string.IsNullOrEmpty(received)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));

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
                MobileAuthCallback.Received -= OnCallbackReceived;
                _activeSubscriber = null;
            }
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
