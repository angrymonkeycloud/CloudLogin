namespace AngryMonkey.CloudLogin;

/// <summary>A registered WebAuthn credential (passkey, device PIN, fingerprint, or face unlock).</summary>
public record CloudLoginPasskey
{
    /// <summary>Base64url-encoded credential id.</summary>
    public string CredentialId { get; set; } = string.Empty;

    public byte[] PublicKey { get; set; } = [];

    /// <summary>
    /// Authenticator signature counter. A counter that fails to advance can indicate a cloned
    /// authenticator, so it is persisted and checked on every assertion.
    /// </summary>
    public uint SignCount { get; set; }

    /// <summary>Name the user gave this credential, e.g. "MacBook Touch Id".</summary>
    public string? Name { get; set; }

    public Guid AaGuid { get; set; }
    public List<string> Transports { get; set; } = [];
    public bool IsBackedUp { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? LastUsedOn { get; set; }
}
