namespace AngryMonkey.CloudLogin;

/// <summary>
/// Material handed to the user exactly once, when enrolling an authenticator app. After the
/// enrollment is confirmed the secret is never returned again.
/// </summary>
public record CloudLoginAuthenticatorEnrollment
{
    /// <summary>Base32 secret, for manual entry when a QR code can't be scanned.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The otpauth:// URI to render as a QR code.</summary>
    public string ProvisioningUri { get; set; } = string.Empty;
}
