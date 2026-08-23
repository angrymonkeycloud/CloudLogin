namespace AngryMonkey.CloudLogin;

/// <summary>
/// Parameters for generating redirect URLs in CloudLogin
/// </summary>
public sealed record CloudLoginRedirectParameters
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
    public static CloudLoginRedirectParameters Create(string controller, string action, string? referer = null)
        => new() { Controller = controller, Action = action, Referer = referer };

    /// <summary>
    /// Creates parameters for a login redirect
    /// </summary>
    public static CloudLoginRedirectParameters CreateLogin(string controller, string action, bool keepMeSignedIn = false, string? referer = null)
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
    public static CloudLoginRedirectParameters CreateCustomLogin(string controller, string action, bool keepMeSignedIn = false, string? referer = null, bool sameSite = false, string? primaryEmail = null, string? userInfo = null, string? inputValue = null)
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
