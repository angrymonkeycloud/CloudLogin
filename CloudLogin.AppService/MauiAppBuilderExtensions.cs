using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using System.Web;

namespace AngryMonkey.CloudLogin;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers CloudLogin for MAUI apps with automatic deep-link/auth callback handling.
    /// Just add one line in MauiProgram.cs:
    /// <code>builder.AddMobileCloudLogin();</code>
    /// Platform-specific callback handling is wired automatically at runtime.
    /// </summary>
    public static MauiAppBuilder AddMobileCloudLogin(this MauiAppBuilder builder)
    {
        builder.Services.AddScoped<ICloudLoginService, CloudLoginAppService>();
        return builder;
    }

    /// <summary>
    /// Parses a callback URI and raises the MobileAuthCallback if a requestId is found.
    /// Call this from platform-specific lifecycle/intent handlers.
    /// </summary>
    public static void HandleCallbackUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, CloudLoginAppService.CallbackScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, CloudLoginAppService.CallbackHost, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, CloudLoginAppService.CallbackPath, StringComparison.OrdinalIgnoreCase))
            return;

        var query = HttpUtility.ParseQueryString(uri.Query);
        string? requestId = query.Get("requestId");

        if (!string.IsNullOrWhiteSpace(requestId))
            MobileAuthCallback.Raise(requestId);
    }
}

