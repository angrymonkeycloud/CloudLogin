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

    /// <summary>True when the host has a code-verification provider configured (email code, WhatsApp).</summary>
    public bool CodeProviderConfigured { get; set; }

    /// <summary>
    /// Whether this authority signs people in itself, rather than only handing them to an external
    /// provider.
    /// </summary>
    /// <remarks>
    /// When it does not, the whole idea of a credential on this account is empty: there is no
    /// password to change, and a second factor or a passkey would guard a sign-in that never
    /// happens here. The account UI hides those sections rather than offering settings that can
    /// never take effect.
    /// </remarks>
    public bool LocalSignInConfigured => PasswordProviderConfigured || CodeProviderConfigured;

    public bool HasAuthenticatorApp { get; set; }
    public DateTimeOffset? AuthenticatorEnrolledOn { get; set; }

    public List<CloudLoginPasskeySummary> Passkeys { get; set; } = [];

    /// <summary>Providers linked to this account, with the input they're linked through.</summary>
    public List<CloudLoginConnectedProvider> ConnectedProviders { get; set; } = [];

    /// <summary>Providers the host supports that this account hasn't linked yet.</summary>
    public List<CloudLoginProviderDefinition> AvailableProviders { get; set; } = [];
}
