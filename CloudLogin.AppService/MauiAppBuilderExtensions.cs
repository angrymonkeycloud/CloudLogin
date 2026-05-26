using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using System.Web;

namespace AngryMonkey.CloudLogin;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers CloudLogin services and automatically wires platform auth-callback
    /// interception for Android and iOS. No platform code needed in the host app.
    /// </summary>
    public static MauiAppBuilder AddMobileCloudLogin(this MauiAppBuilder builder)
    {
        builder.Services.AddScoped<ICloudLoginService, CloudLoginAppService>();

        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android =>
            {
                android.OnCreate((activity, _) => HandleAndroidIntent(activity.Intent));
                android.OnNewIntent((_, intent) => HandleAndroidIntent(intent));
            });
#endif
#if IOS || MACCATALYST
            events.AddiOS(ios =>
            {
                ios.OpenUrl((_, url, _2) =>
                    Uri.TryCreate(url?.AbsoluteString, UriKind.Absolute, out var uri) && HandleCallbackUri(uri));

                ios.ContinueUserActivity((_, activity, _2) =>
                    activity?.WebPageUrl != null &&
                    Uri.TryCreate(activity.WebPageUrl.AbsoluteString, UriKind.Absolute, out var uri2) &&
                    HandleCallbackUri(uri2));
            });
#endif
        });

        return builder;
    }

#if ANDROID
    private static void HandleAndroidIntent(Android.Content.Intent? intent)
    {
        try
        {
            if (intent?.Data == null) return;
            if (Uri.TryCreate(intent.Data.ToString(), UriKind.Absolute, out var uri))
                HandleCallbackUri(uri);
        }
        catch { }
    }
#endif

    /// <summary>
    /// Checks whether the URI is a CloudLogin auth callback and, if so, raises
    /// MobileAuthCallback and returns true - callers should stop further processing.
    /// </summary>
    public static bool HandleCallbackUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, CloudLoginAppService.CallbackScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, CloudLoginAppService.CallbackHost, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, CloudLoginAppService.CallbackPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var query = HttpUtility.ParseQueryString(uri.Query);
        string? requestId = query.Get("requestId");

        if (!string.IsNullOrWhiteSpace(requestId))
            MobileAuthCallback.Raise(requestId);

        return true;
    }
}
