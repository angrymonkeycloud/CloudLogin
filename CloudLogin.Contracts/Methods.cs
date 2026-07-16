using System.Web;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Parameters for generating redirect URLs in CloudLogin
/// </summary>
public sealed record RedirectParameters
{
    public required string Controller { get; init; }
    public required string Action { get; init; }
    public string? KeepMeSignedIn { get; init; }
    public string? RedirectUri { get; init; } // OAuth provider redirect URI only
    public string? SameSite { get; init; }
    public string? PrimaryEmail { get; init; }
    public string? UserInfo { get; init; }
    public string? InputValue { get; init; }
    public string? Referer { get; init; } // External website URL

    /// <summary>
    /// Creates parameters for a basic redirect
    /// </summary>
    public static RedirectParameters Create(string controller, string action, string? referer = null)
        => new() { Controller = controller, Action = action, Referer = referer };

    /// <summary>
    /// Creates parameters for a login redirect
    /// </summary>
    public static RedirectParameters CreateLogin(string controller, string action, bool keepMeSignedIn = false, string? referer = null)
        => new()
        {
            Controller = controller,
            Action = action,
            KeepMeSignedIn = keepMeSignedIn.ToString().ToLowerInvariant(),
            Referer = referer
        };

    /// <summary>
    /// Creates parameters for a custom login redirect
    /// </summary>
    public static RedirectParameters CreateCustomLogin(string controller, string action, bool keepMeSignedIn = false, string? referer = null, bool sameSite = false, string? primaryEmail = null, string? userInfo = null, string? inputValue = null)
        => new()
        {
            Controller = controller,
            Action = action,
            KeepMeSignedIn = keepMeSignedIn.ToString().ToLowerInvariant(),
            Referer = referer,
            SameSite = sameSite.ToString().ToLowerInvariant(),
            PrimaryEmail = primaryEmail,
            UserInfo = userInfo,
            InputValue = inputValue
        };
}

/// <summary>
/// Parameters for authentication operations
/// </summary>
public sealed record AuthParameters
{
    public bool KeepMeSignedIn { get; init; }
    public string Referer { get; init; } = string.Empty; // Changed from RedirectUri
    public bool SameSite { get; init; }
    public string PrimaryEmail { get; init; } = string.Empty;
    public string? UserInfo { get; init; }
    public string? Input { get; init; }

    public static AuthParameters Create(bool keepMeSignedIn = false, string referer = "", bool sameSite = false, string primaryEmail = "", string? userInfo = null, string? input = null)
        => new()
        {
            KeepMeSignedIn = keepMeSignedIn,
            Referer = referer,
            SameSite = sameSite,
            PrimaryEmail = primaryEmail,
            UserInfo = userInfo,
            Input = input
        };
}

/// <summary>
/// Secure utility class for CloudLogin URL generation and routing
/// </summary>
public static class CloudLoginShared
{
    /// <summary>
    /// Generates a secure redirect URL with proper encoding
    /// </summary>
    /// <param name="parameters">The redirect parameters</param>
    /// <returns>A properly encoded URL string</returns>
    public static string RedirectString(RedirectParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Action);

        string path = $"/{parameters.Controller}/{parameters.Action}";
        List<string> queryParams = [];

        AddParameter(queryParams, "keepMeSignedIn", parameters.KeepMeSignedIn);
        AddParameter(queryParams, "redirectUri", parameters.RedirectUri); // OAuth provider redirect URI
        AddParameter(queryParams, "sameSite", parameters.SameSite);
        AddParameter(queryParams, "primaryEmail", parameters.PrimaryEmail);
        AddParameter(queryParams, "userInfo", parameters.UserInfo);
        AddParameter(queryParams, "input", parameters.InputValue);
        AddParameter(queryParams, "referer", parameters.Referer); // External website URL

        return queryParams.Count > 0 ? $"{path}?{string.Join("&", queryParams)}" : path;
    }

    private static void AddParameter(List<string> queryParams, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            queryParams.Add($"{name}={HttpUtility.UrlEncode(value)}");
    }

    /// <summary>
    /// Validates a redirect URI to prevent open redirect attacks
    /// More permissive version for external website integration
    /// </summary>
    /// <param name="redirectUri">The URI to validate</param>
    /// <param name="allowedDomains">List of allowed domains (optional)</param>
    /// <returns>True if the URI is safe to redirect to</returns>
    public static bool IsValidRedirectUri(string? redirectUri, IEnumerable<string>? allowedDomains = null)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return false;

        // Allow relative URLs (they're safe)
        if (redirectUri.StartsWith('/') && !redirectUri.StartsWith("//"))
            return true;

        // Allow common mobile app schemes
        if (IsValidMobileScheme(redirectUri))
            return true;

        // For absolute URLs, validate against security rules
        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri))
        {
            // Block dangerous schemes
            if (IsDangerousScheme(uri.Scheme))
                return false;

            // If specific domains are allowed, check against them
            if (allowedDomains?.Any() == true)
                return IsAllowedDomain(uri, allowedDomains);

            // For external websites calling CloudLogin, be more permissive
            return IsAllowedSchemeAndHost(uri);
        }

        return false;
    }

    /// <summary>
    /// Checks if the URI scheme is valid for mobile applications
    /// </summary>
    private static bool IsValidMobileScheme(string redirectUri)
    {
        // Common mobile app schemes
        if (redirectUri.StartsWith("myapp://", StringComparison.OrdinalIgnoreCase) ||
            redirectUri.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
            return true;

        // Generic mobile scheme pattern (appname://)
        return System.Text.RegularExpressions.Regex.IsMatch(redirectUri, @"^[a-zA-Z][a-zA-Z0-9+.-]*://", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Checks if the URI scheme is dangerous (javascript, data, etc.)
    /// </summary>
    private static bool IsDangerousScheme(string scheme)
    {
        return scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("data", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("vbscript", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("file", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the domain is in the allowed list
    /// </summary>
    private static bool IsAllowedDomain(Uri uri, IEnumerable<string> allowedDomains)
    {
        return allowedDomains.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if the scheme and host are generally allowed
    /// More permissive for external website integration
    /// </summary>
    private static bool IsAllowedSchemeAndHost(Uri uri)
    {
        // Always allow HTTPS
        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return true;

        // Allow HTTP for development environments
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("127.0.0.1") ||
                   uri.Host.StartsWith("192.168.") ||
                   uri.Host.StartsWith("10.") ||
                   uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Creates a configuration-aware redirect URI validator
    /// Useful for production environments with specific domain restrictions
    /// </summary>
    public static Func<string?, bool> CreateValidator(IEnumerable<string>? allowedDomains = null, bool allowDevelopmentUrls = true)
    {
        return redirectUri =>
        {
            if (!allowDevelopmentUrls && !string.IsNullOrEmpty(redirectUri))
            {
                // In production, be more strict
                if (Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri))
                {
                    if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                        return false; // No HTTP in production
                }
            }

            return IsValidRedirectUri(redirectUri, allowedDomains);
        };
    }
}