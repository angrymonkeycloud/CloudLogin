using AngryMonkey.CloudLogin.Sever.Providers;
using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin.Server;

public static partial class CloudLoginConfigurationValidator
{
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*$")]
    private static partial Regex UriSchemePattern();

    public static void Validate(CloudLoginWebConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Security);

        CloudLoginSecurityOptions security = configuration.Security;

        if (!isDevelopment && !security.RequireHttps)
            throw new InvalidOperationException("CloudLogin HTTPS enforcement cannot be disabled outside Development.");

        if (configuration.LoginDuration <= TimeSpan.Zero || configuration.LoginDuration > TimeSpan.FromDays(90))
            throw new InvalidOperationException("LoginDuration must be greater than zero and no longer than 90 days.");

        if (security.SessionIdleTimeout <= TimeSpan.Zero || security.SessionIdleTimeout > TimeSpan.FromDays(1))
            throw new InvalidOperationException("Security.SessionIdleTimeout must be greater than zero and no longer than 24 hours.");

        if (security.MinimumPasswordLength < 8 ||
            security.MaximumPasswordLength < security.MinimumPasswordLength ||
            security.MaximumPasswordLength > 1024)
            throw new InvalidOperationException("Password length limits are invalid.");

        if (security.PasswordHashIterations < CloudLoginSecurityOptions.MinimumPbkdf2Iterations)
            throw new InvalidOperationException($"PasswordHashIterations must be at least {CloudLoginSecurityOptions.MinimumPbkdf2Iterations:N0}.");

        if (security.AuthenticationPermitLimit <= 0 || security.AuthenticationWindow <= TimeSpan.Zero)
            throw new InvalidOperationException("Authentication rate-limit settings must be greater than zero.");

        if (security.MaximumProfileImageBytes < 1024 || security.MaximumProfileImageBytes > 20 * 1024 * 1024)
            throw new InvalidOperationException("MaximumProfileImageBytes must be between 1 KB and 20 MB.");

        if (string.IsNullOrWhiteSpace(configuration.CookieName))
            throw new InvalidOperationException("CookieName is required.");

        if (!string.IsNullOrWhiteSpace(configuration.CookieDomain) &&
            configuration.CookieName.StartsWith("__Host-", StringComparison.Ordinal))
            throw new InvalidOperationException("A __Host- cookie cannot specify CookieDomain. Change CookieName only when domain-wide cookies are explicitly required.");

        foreach (string origin in configuration.AllowedRedirectOrigins)
            ValidateOrigin(origin);

        foreach (string scheme in configuration.AllowedMobileSchemes)
        {
            if (!UriSchemePattern().IsMatch(scheme) ||
                scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Allowed mobile scheme '{scheme}' is invalid.");
        }

        bool hasLegacyCodeProvider = configuration.Providers.Any(provider =>
            provider.IsCodeVerification && !provider.IsExternal);

        if (hasLegacyCodeProvider && !security.EnableLegacyClientVerificationCodes)
            throw new InvalidOperationException(
                "A client-managed verification-code provider is configured. This legacy flow is disabled by default because verification occurs in browser code.");

        if (security.EnableLegacyClientVerificationCodes && !isDevelopment)
            throw new InvalidOperationException("Legacy client-managed verification codes cannot be enabled outside Development.");
    }

    private static void ValidateOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)))
            throw new InvalidOperationException($"Allowed redirect origin '{origin}' must be an HTTP(S) origin without a path, query, credentials, or fragment.");

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException($"Allowed redirect origin '{origin}' must use HTTPS unless it is loopback development traffic.");
    }
}
