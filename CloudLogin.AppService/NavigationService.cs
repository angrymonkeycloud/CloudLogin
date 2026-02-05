using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AngryMonkey.CloudLogin;

public class NavigationService(NavigationManager navigationmanager, IJSRuntime jsRuntime) : NavigationServiceBase(navigationmanager)
{

    private readonly IJSRuntime _jsRuntime = jsRuntime;
    public override bool IsWebPlatform => false;


    public override bool TryNavigateBack()
    {
        if (!ShouldShowBackButton && !IsPopupOpen) return false;
        _ = Task.Run(NavigateBackAsync);
        return true;
    }

    public override async Task NavigateToAsync(string route, bool forceReload = false)
    {
        _navigationManager.NavigateTo(route, forceLoad: forceReload, replace: false);
        await Task.CompletedTask;
    }

    public override async Task NavigateToExternalAsync(string url, bool newTab = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            string trimmed = url.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme.Equals("tel", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("geo", StringComparison.OrdinalIgnoreCase)))
            {
                await Launcher.OpenAsync(uri);
                return;
            }
            if (!trimmed.Contains(' ') && (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || trimmed.Contains('.')))
            {
                string httpUrl = $"https://{trimmed}";
                if (Uri.TryCreate(httpUrl, UriKind.Absolute, out Uri? httpUri))
                {
                    await Launcher.OpenAsync(httpUri);
                    return;
                }
            }
            _navigationManager.NavigateTo(trimmed, forceLoad: true);
        }
        catch
        {
            try { _navigationManager.NavigateTo(url, forceLoad: true); } catch { }
        }
    }

    public override async Task NavigateBackAsync()
    {
        try
        {
            if (IsPopupOpen)
            {
                await _jsRuntime.InvokeVoidAsync("history.back");
                return;
            }
            await _jsRuntime.InvokeVoidAsync("history.back");
        }
        catch
        {
            _navigationManager.NavigateTo("/", forceLoad: true);
        }
    }

    public override bool TryHandleDeepLink(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return TryHandleDeepLink(u);
        return false;
    }
}
