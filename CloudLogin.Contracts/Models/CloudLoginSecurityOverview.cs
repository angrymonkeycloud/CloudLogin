namespace AngryMonkey.CloudLogin;

/// <summary>
/// Safe projection of a user's security state for display. Contains no secret material:
/// no TOTP key, no passkey public keys.
/// </summary>
public record CloudLoginSecurityOverview
{
    /// <summary>True when a password credential exists and can be changed rather than created.</summary>
    public bool HasPassword { get; set; }

    /// <summary>True when the host has the password provider configured at all.</summary>
    public bool PasswordProviderConfigured { get; set; }

    public bool HasAuthenticatorApp { get; set; }
    public DateTimeOffset? AuthenticatorEnrolledOn { get; set; }

    public List<CloudLoginPasskeySummary> Passkeys { get; set; } = [];

    /// <summary>Providers linked to this account, with the input they're linked through.</summary>
    public List<CloudLoginConnectedProvider> ConnectedProviders { get; set; } = [];

    /// <summary>Providers the host supports that this account hasn't linked yet.</summary>
    public List<CloudLoginProviderDefinition> AvailableProviders { get; set; } = [];
}
