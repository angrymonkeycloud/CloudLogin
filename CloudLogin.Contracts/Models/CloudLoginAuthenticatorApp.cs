namespace AngryMonkey.CloudLogin;

/// <summary>A TOTP authenticator-app enrollment.</summary>
public record CloudLoginAuthenticatorApp
{
    /// <summary>Base32 shared secret. Never leaves the server once enrollment is confirmed.</summary>
    public string SecretKey { get; set; } = string.Empty;

    public DateTimeOffset EnrolledOn { get; set; }

    /// <summary>
    /// False until the user proves possession by entering a generated code. Unconfirmed
    /// enrollments are not treated as an active second factor.
    /// </summary>
    public bool IsConfirmed { get; set; }
}
