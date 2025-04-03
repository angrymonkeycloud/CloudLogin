using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AngryMonkey.CloudLogin;

public class CloudLoginJS(IJSRuntime jsRuntime, NavigationManager navigationManager)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private readonly NavigationManager _navigationManager = navigationManager;

    public async Task<User?> CurrentUser(string? baseUrl = null)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<User>("cloudLogin.getCurrentUser", baseUrl ?? _navigationManager.BaseUri);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error fetching current user: {ex.Message}");
            return null;
        }
    }
}