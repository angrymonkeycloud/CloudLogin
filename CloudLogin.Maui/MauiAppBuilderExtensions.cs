using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using System.Web;

namespace AngryMonkey.CloudLogin;

public static class MauiAppBuilderExtensions
{
    private static MauiCloudLoginOptions? _configuredOptions;

    /// <summary>
    /// Registers CloudLogin services and automatically wires platform auth-callback
    /// interception for Android and iOS. No platform code needed in the host app.
    /// </summary>
    /// <param name="loginUrl">
    /// Base address of the CloudLogin server that issues this app's user identities. Pass the
    /// same value the app's backend uses for <c>LoginUrl</c>, otherwise the app signs in against
    /// a different user store and the ids it receives will not match the backend's role tables.
    /// When null, <see cref="CloudLoginBaseService.LoginBaseUrl"/> keeps its default.
    /// </param>
    public static MauiAppBuilder AddMauiCloudLogin(this MauiAppBuilder builder, string loginUrl, string callbackScheme)
        => builder.AddMauiCloudLogin(new MauiCloudLoginOptions
        {
            LoginUrl = loginUrl,
            CallbackScheme = callbackScheme
        });

    public static MauiAppBuilder AddMauiCloudLogin(this MauiAppBuilder builder, MauiCloudLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _configuredOptions = options;
        CloudLoginBaseService.LoginBaseUrl = options.LoginUrl;

        builder.Services.AddSingleton(options);
        builder.Services.AddScoped<ICloudLoginService, MauiCloudLoginService>();

        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android =>
            {
                android.OnCreate((activity, _) => HandleAndroidIntent(activity.Intent, options));
                android.OnNewIntent((_, intent) => HandleAndroidIntent(intent, options));
            });
#endif
#if IOS || MACCATALYST
            events.AddiOS(ios =>
            {
                ios.OpenUrl((_, url, _2) =>
                    Uri.TryCreate(url?.AbsoluteString, UriKind.Absolute, out var uri) && HandleCallbackUri(uri, options));

                ios.ContinueUserActivity((_, activity, _2) =>
                    activity?.WebPageUrl != null &&
                    Uri.TryCreate(activity.WebPageUrl.AbsoluteString, UriKind.Absolute, out var uri2) &&
                    HandleCallbackUri(uri2, options));
            });
#endif
        });

        return builder;
    }

#if ANDROID
    private static void HandleAndroidIntent(Android.Content.Intent? intent, MauiCloudLoginOptions options)
    {
        try
        {
            if (intent?.Data == null) return;
            if (Uri.TryCreate(intent.Data.ToString(), UriKind.Absolute, out var uri))
                HandleCallbackUri(uri, options);
        }
        catch { }
    }
#endif

    /// <summary>
    /// Checks whether the URI is a CloudLogin auth callback and, if so, raises
    /// MobileAuthCallback and returns true - callers should stop further processing.
    /// </summary>
    public static bool HandleCallbackUri(Uri uri)
        => _configuredOptions is not null && HandleCallbackUri(uri, _configuredOptions);

    public static bool HandleCallbackUri(Uri uri, MauiCloudLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(uri.Scheme, options.CallbackScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, options.CallbackHost, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, options.CallbackPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var query = HttpUtility.ParseQueryString(uri.Query);
        string? requestId = query.Get("requestId");

        if (!string.IsNullOrWhiteSpace(requestId))
            MobileAuthCallback.Raise(requestId);

        return true;
    }
}
