using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin;

public sealed class MauiCloudLoginOptions
{
    /// <summary>
    /// The application's own backend, which is the CloudLogin client. The system browser signs in through it, and the session it builds is
    /// handed to this app. Nothing about CloudLogin itself needs configuring here: the backend knows its authority and its credential.
    /// </summary>
    public required string ApplicationUrl { get; set; }

    /// <summary>Optional: the CloudLogin authority, only for opening its account page. Sign-in and sign-out do not use it.</summary>
    public string? LoginUrl { get; set; }

    public required string CallbackScheme { get; set; }
    public string CallbackHost { get; set; } = "auth";
    public string CallbackPath { get; set; } = "/callback";
    public string StorageKeyPrefix { get; set; } = "angrymonkey.cloudlogin";

    public string CallbackUrl => $"{CallbackScheme}://{CallbackHost}{CallbackPath}";

    internal string StorageKey(string suffix) => $"{StorageKeyPrefix}.{suffix}";

    internal void Validate()
    {
        if (!IsHttpUrl(ApplicationUrl))
            throw new ArgumentException("ApplicationUrl must be an absolute HTTP or HTTPS URL.", nameof(ApplicationUrl));

        if (!string.IsNullOrWhiteSpace(LoginUrl) && !IsHttpUrl(LoginUrl))
            throw new ArgumentException("LoginUrl must be an absolute HTTP or HTTPS URL.", nameof(LoginUrl));

        if (!Regex.IsMatch(CallbackScheme, "^[a-zA-Z][a-zA-Z0-9+.-]*$"))
            throw new ArgumentException("CallbackScheme is not a valid URI scheme.", nameof(CallbackScheme));

        ArgumentException.ThrowIfNullOrWhiteSpace(CallbackHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(StorageKeyPrefix);

        if (!CallbackPath.StartsWith('/'))
            CallbackPath = $"/{CallbackPath}";

        ApplicationUrl = ApplicationUrl.TrimEnd('/');
        LoginUrl = string.IsNullOrWhiteSpace(LoginUrl) ? null : LoginUrl.TrimEnd('/');
        CallbackScheme = CallbackScheme.ToLowerInvariant();
        CallbackHost = CallbackHost.ToLowerInvariant();
        StorageKeyPrefix = StorageKeyPrefix.Trim().Trim('.');
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
