namespace AngryMonkey.CloudLogin;

/// <summary>
/// Per-user security credentials, stored separately from the user record so that secrets
/// (TOTP keys, passkey material) are never part of the <see cref="CloudUser"/> that the
/// browser receives.
/// </summary>
public record CloudLoginUserSecurityDocument
{
    public Guid UserId { get; set; }
    public List<CloudLoginPasskey> Passkeys { get; set; } = [];
    public CloudLoginAuthenticatorApp? Authenticator { get; set; }
}
