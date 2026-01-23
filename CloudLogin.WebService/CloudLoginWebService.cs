using AngryMonkey.CloudLogin;
using CloudLogin.Shared;
using CloudLogin.Shared.CloudLoginServices;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;


namespace CloudLogin.WebService;

public class CloudLoginWebService(NavigationManager navigationManager, IJSRuntime js) : CloudLoginBaseService
{
    private readonly NavigationManager _navigationManager = navigationManager;
    private readonly IJSRuntime _js = js;

    private const string LocalStorageUserKey = "coverbox_user_json";

    private bool _initialized;
    private Task? _initTask;

    private Task EnsureInitializedAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        _initialized = true;
        _initTask = InitializeAsync();
        return _initTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Subscribe to persist changes
            UserChanged += OnUserChangedAsync;

            // Load any cached user from localStorage
            string? cached = await SafeLocalStorageGetString(LocalStorageUserKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                try
                {
                    UserModel? loaded = JsonSerializer.Deserialize<UserModel>(cached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (loaded is not null)
                    {
                        User = loaded;
                        RaiseUserChanged(User);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Web AccountService] Failed to parse cached user: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Web AccountService] InitializeAsync error: {ex.Message}");
        }
    }

    private async void OnUserChangedAsync(UserModel? user)
    {
        try
        {
            if (user is null)
                await SafeLocalStorageRemove(LocalStorageUserKey);
            else
            {
                string json = JsonSerializer.Serialize(user);
                await SafeLocalStorageSetString(LocalStorageUserKey, json);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Web AccountService] Persist user failed: {ex.Message}");
        }
    }

    // Optional helper to refresh the user from the site backend
    public async Task<UserModel?> RefreshUserAsync()
    {
        await EnsureInitializedAsync();

        UserModel? newUser = null;

        try
        {
            using HttpClient httpClient = new() { BaseAddress = new Uri(_navigationManager.BaseUri) };
            HttpResponseMessage response = await httpClient.GetAsync("api/users/getUser");

            if (response.IsSuccessStatusCode)
            {
                newUser = await response.Content.ReadFromJsonAsync<UserModel>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Web AccountService] Error refreshing user: {ex.Message}");
        }

        Guid? previousId = User?.ID;

        if (newUser is not null)
        {
            User = newUser; // Resets RequestId in base

            if (newUser.ID != previousId)
                RaiseUserChanged(User);
        }
        else
        {
            if (User is not null)
            {
                OnUserSignedOut();
                RaiseUserChanged(null);
            }
        }

        return User;
    }

    public override async Task Login()
    {
        await EnsureInitializedAsync();
        // If already signed in, do nothing
        if (User is not null)
            return;

        // If current URL already is /signin, don't loop
        var abs = _navigationManager.Uri;
        if (abs.Contains("/signin", StringComparison.OrdinalIgnoreCase))
            return;

        // Navigate to centralized sign-in page
        var rel = "/";
        try
        {
            var u = new Uri(abs);
            rel = u.GetLeftPart(UriPartial.Path).Replace(_navigationManager.BaseUri.TrimEnd('/'), "");
            if (string.IsNullOrWhiteSpace(rel)) rel = "/"; else if (!rel.StartsWith('/')) rel = "/" + rel;
        }
        catch { rel = "/"; }

        _navigationManager.NavigateTo($"/signin?returnUrl={Uri.EscapeDataString(rel)}");
        await Task.CompletedTask;
    }

    public override async Task BeginLoginAsync(string? returnUrl)
    {
        await EnsureInitializedAsync();
        if (User != null)
            return;
        var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        var authUrl = $"/auth/login?returnUrl={Uri.EscapeDataString(target)}";
        _navigationManager.NavigateTo(authUrl, forceLoad: true);
    }

    public override async Task Logout()
    {
        await EnsureInitializedAsync();
        try
        {
            using HttpClient httpClient = new() { BaseAddress = new Uri(_navigationManager.BaseUri) };
            await httpClient.GetAsync("api/users/logout");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Web AccountService] Error during logout: {ex.Message}");
        }

        OnUserSignedOut();
        RaiseUserChanged(null);
        _navigationManager.NavigateTo("/", forceLoad: true);
        await Task.CompletedTask;
    }

    public override async Task<string> ProfileUrl()
    {
        await EnsureInitializedAsync();
        string returnUrl = Uri.EscapeDataString(_navigationManager.Uri);
        return $"/auth/profile?returnUrl={returnUrl}";
    }

    private async Task SafeLocalStorageSetString(string key, string value)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { /* no-op */ }
    }

    private async Task<string?> SafeLocalStorageGetString(string key)
    {
        try { return await _js.InvokeAsync<string?>("localStorage.getItem", key); }
        catch { return null; }
    }

    private async Task SafeLocalStorageRemove(string key)
    {
        try { await _js.InvokeVoidAsync("localStorage.removeItem", key); }
        catch { /* no-op */ }
    }


}
