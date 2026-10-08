using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using System.Web;

namespace AngryMonkey.CloudLogin;

public static class MauiAppBuilderExtensions
{
    private static MauiCloudLoginOptions? _configuredOptions;

    /// <summary>
    /// Registers CloudLogin sign-in for a native app and wires platform auth-callback interception for Android and iOS. No platform code
    /// is needed in the host app.
    /// </summary>
    /// <param name="applicationUrl">
    /// The app's own backend. It is the CloudLogin client: the system browser signs in through it and it hands the session back to this app,
    /// so the app needs no credential and the authority needs no registration of the app. The backend lists the app's scheme in
    /// <c>CloudLoginServerConfiguration.NativeCallbackSchemes</c>.
    /// </param>
    /// <param name="callbackScheme">The custom URL scheme this app registers on the device, for example <c>blusky</c>.</param>
    /// <remarks>
    /// The app must also register an <see cref="IMauiCloudLoginNativeExchange"/>, which keeps the session cookie the exchange returns.
    /// </remarks>
    public static MauiAppBuilder AddMauiCloudLogin(this MauiAppBuilder builder, string applicationUrl, string callbackScheme)
        => builder.AddMauiCloudLogin(new MauiCloudLoginOptions
        {
            ApplicationUrl = applicationUrl,
            CallbackScheme = callbackScheme
        });

    public static MauiAppBuilder AddMauiCloudLogin(this MauiAppBuilder builder, MauiCloudLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _configuredOptions = options;

        if (options.LoginUrl is not null)
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

        if (!string.IsNullOrWhiteSpace(query.Get("handoff")) || !string.IsNullOrWhiteSpace(query.Get("error")))
            MobileAuthCallback.Raise(new MauiAuthCallback(query.Get("handoff"), query.Get("state"), query.Get("operation"), query.Get("error")));

        return true;
    }
}
