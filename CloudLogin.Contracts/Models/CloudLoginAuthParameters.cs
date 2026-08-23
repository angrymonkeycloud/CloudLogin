namespace AngryMonkey.CloudLogin;

/// <summary>
/// Parameters for authentication operations
/// </summary>
public sealed record CloudLoginAuthParameters
{
    public bool KeepMeSignedIn { get; init; }
    public string Referer { get; init; } = string.Empty; // Changed from RedirectUri
    public bool SameSite { get; init; }
    public string PrimaryEmail { get; init; } = string.Empty;
    public string? UserInfo { get; init; }
    public string? Input { get; init; }

    public static CloudLoginAuthParameters Create(bool keepMeSignedIn = false, string referer = "", bool sameSite = false, string primaryEmail = "", string? userInfo = null, string? input = null)
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
