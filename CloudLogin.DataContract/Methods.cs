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
    public string? RedirectUri { get; init; }
    public string? SameSite { get; init; }
    public string? ActionState { get; init; }
    public string? PrimaryEmail { get; init; }
    public string? UserInfo { get; init; }
    public string? InputValue { get; init; }

    /// <summary>
    /// Creates parameters for a basic redirect
    /// </summary>
    public static RedirectParameters Create(string controller, string action)
        => new() { Controller = controller, Action = action };

    /// <summary>
    /// Creates parameters for a login redirect
    /// </summary>
    public static RedirectParameters CreateLogin(string controller, string action, bool keepMeSignedIn = false, string? redirectUri = null)
        => new()
        {
            Controller = controller,
            Action = action,
            KeepMeSignedIn = keepMeSignedIn.ToString().ToLowerInvariant(),
            RedirectUri = redirectUri
        };

    /// <summary>
    /// Creates parameters for a custom login redirect
    /// </summary>
    public static RedirectParameters CreateCustomLogin(string controller, string action, bool keepMeSignedIn = false, string? redirectUri = null, bool sameSite = false, string? actionState = null, string? primaryEmail = null, string? userInfo = null, string? inputValue = null)
        => new()
        {
            Controller = controller,
            Action = action,
            KeepMeSignedIn = keepMeSignedIn.ToString().ToLowerInvariant(),
            RedirectUri = redirectUri,
            SameSite = sameSite.ToString().ToLowerInvariant(),
            ActionState = actionState,
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
    public string RedirectUri { get; init; } = string.Empty;
    public bool SameSite { get; init; }
    public string ActionState { get; init; } = string.Empty;
    public string PrimaryEmail { get; init; } = string.Empty;
    public string? UserInfo { get; init; }
    public string? Input { get; init; }

    public static AuthParameters Create(bool keepMeSignedIn = false, string redirectUri = "", bool sameSite = false, string actionState = "", string primaryEmail = "", string? userInfo = null, string? input = null)
        => new()
        {
            KeepMeSignedIn = keepMeSignedIn,
            RedirectUri = redirectUri,
            SameSite = sameSite,
            ActionState = actionState,
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
        AddParameter(queryParams, "redirectUri", parameters.RedirectUri);
        AddParameter(queryParams, "sameSite", parameters.SameSite);
        AddParameter(queryParams, "actionState", parameters.ActionState);
        AddParameter(queryParams, "primaryEmail", parameters.PrimaryEmail);
        AddParameter(queryParams, "userInfo", parameters.UserInfo);
        AddParameter(queryParams, "input", parameters.InputValue);

        return queryParams.Count > 0 ? $"{path}?{string.Join("&", queryParams)}" : path;
    }

    /// <summary>
    /// Legacy method for backward compatibility - use RedirectString(RedirectParameters) instead
    /// </summary>
    [Obsolete("Use RedirectString(RedirectParameters parameters) instead")]
    public static string RedirectString(string controller, string action, string? keepMeSignedIn = null, string? redirectUri = null, string? sameSite = null, string? actionState = null, string? primaryEmail = null, string? userInfo = null, string? inputValue = null)
    {
        RedirectParameters parameters = new()
        {
            Controller = controller,
            Action = action,
            KeepMeSignedIn = keepMeSignedIn,
            RedirectUri = redirectUri,
            SameSite = sameSite,
            ActionState = actionState,
            PrimaryEmail = primaryEmail,
            UserInfo = userInfo,
            InputValue = inputValue
        };

        return RedirectString(parameters);
    }

    private static void AddParameter(List<string> queryParams, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            queryParams.Add($"{name}={HttpUtility.UrlEncode(value)}");
    }

    /// <summary>
    /// Validates a redirect URI to prevent open redirect attacks
    /// </summary>
    /// <param name="redirectUri">The URI to validate</param>
    /// <param name="allowedDomains">List of allowed domains (optional)</param>
    /// <returns>True if the URI is safe to redirect to</returns>
    public static bool IsValidRedirectUri(string? redirectUri, IEnumerable<string>? allowedDomains = null)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return false;

        // Allow relative URLs
        if (redirectUri.StartsWith('/') && !redirectUri.StartsWith("//"))
            return true;

        // For absolute URLs, validate against allowed domains
        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri))
        {
            // Block javascript: and data: schemes
            if (uri.Scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("vbscript", StringComparison.OrdinalIgnoreCase))
                return false;

            // If allowed domains are specified, check against them
            if (allowedDomains?.Any() == true)
                return allowedDomains.Any(domain =>
                    uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));

            // Default to allowing https and http
            return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}