using AngryMonkey.Cloud;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;


namespace AngryMonkey.CloudLogin;

public class CloudLoginJS(IJSRuntime jsRuntime, NavigationManager navigationManager)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private readonly NavigationManager _navigationManager = navigationManager;

    public async Task<User?> CurrentUser(string? baseUrl = null)
    {
        try
        {
            var user = await _jsRuntime.InvokeAsync<User>("cloudLogin.getCurrentUser", baseUrl ?? _navigationManager.BaseUri);
            return user;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error fetching current user: {ex.Message}");
            return null;
        }
    }
}