namespace AngryMonkey.CloudLogin;

/// <summary>A provider currently linked to the account.</summary>
public record CloudLoginConnectedProvider
{
    public string Code { get; set; } = string.Empty;
    public string? Label { get; set; }

    /// <summary>The email address or phone number this provider is linked through.</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// False when unlinking would leave the account with no way to sign in. The account page
    /// disables disconnect in that case rather than letting the user lock themselves out.
    /// </summary>
    public bool CanDisconnect { get; set; }
}
