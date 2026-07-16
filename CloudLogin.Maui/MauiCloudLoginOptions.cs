using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin;

public sealed class MauiCloudLoginOptions
{
    public required string LoginUrl { get; set; }
    public required string CallbackScheme { get; set; }
    public string CallbackHost { get; set; } = "auth";
    public string CallbackPath { get; set; } = "/callback";
    public string StorageKeyPrefix { get; set; } = "angrymonkey.cloudlogin";

    public string CallbackUrl => $"{CallbackScheme}://{CallbackHost}{CallbackPath}";

    internal string StorageKey(string suffix) => $"{StorageKeyPrefix}.{suffix}";

    internal void Validate()
    {
        if (!Uri.TryCreate(LoginUrl, UriKind.Absolute, out Uri? loginUri) ||
            (loginUri.Scheme != Uri.UriSchemeHttps && loginUri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("LoginUrl must be an absolute HTTP or HTTPS URL.", nameof(LoginUrl));

        if (!Regex.IsMatch(CallbackScheme, "^[a-zA-Z][a-zA-Z0-9+.-]*$"))
            throw new ArgumentException("CallbackScheme is not a valid URI scheme.", nameof(CallbackScheme));

        ArgumentException.ThrowIfNullOrWhiteSpace(CallbackHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(StorageKeyPrefix);

        if (!CallbackPath.StartsWith('/'))
            CallbackPath = $"/{CallbackPath}";

        LoginUrl = LoginUrl.TrimEnd('/');
        CallbackScheme = CallbackScheme.ToLowerInvariant();
        CallbackHost = CallbackHost.ToLowerInvariant();
        StorageKeyPrefix = StorageKeyPrefix.Trim().Trim('.');
    }
}
