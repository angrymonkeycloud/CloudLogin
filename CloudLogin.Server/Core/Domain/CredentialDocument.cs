namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>Kinds of credential documents stored in the <c>Credentials</c> container.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum CredentialKinds
{
    Password,
    Passkey,
    Totp,
    ExternalIdentity,
    Recovery
}

/// <summary>
/// A credential in the <c>Credentials</c> container (partition key <c>/userId</c>).
/// <para>
/// One document per credential, so revoking a passkey or rotating a password touches exactly one
/// small document and the user profile document never carries secret material. Credential
/// documents are never returned through any API in any version; APIs expose at most derived
/// summaries (for example a passkey's name and creation date).
/// </para>
/// </summary>
public sealed class CredentialDocument : CloudLoginCoreDocument, IExpiringDocument
{
    /// <summary>Partition key.</summary>
    public string UserId { get; set; } = string.Empty;

    public CredentialKinds Kind { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }

    // ── Password (Kind = Password) ────────────────────────────────────────────

    /// <summary>Versioned PBKDF2 hash. Never serialized into tickets or API responses.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// The contact this password is registered against, by immutable
    /// <see cref="UserContact.ContactId"/> — never by the email address or phone number itself.
    /// Null for account-wide credentials.
    /// <para>
    /// A credential keyed on the address breaks the moment the address is corrected or
    /// re-normalized: the key moves and the credential is orphaned. The contact id does not move.
    /// </para>
    /// </summary>
    public Guid? ContactId { get; set; }

    // ── Passkey (Kind = Passkey) ──────────────────────────────────────────────

    public string? PasskeyCredentialId { get; set; }
    public string? PasskeyPublicKey { get; set; }
    public uint? PasskeySignCount { get; set; }
    public string? PasskeyName { get; set; }
    public string? PasskeyAaGuid { get; set; }
    public List<string>? PasskeyTransports { get; set; }
    public bool? PasskeyIsBackedUp { get; set; }
    public DateTimeOffset? PasskeyLastUsedOn { get; set; }

    // ── Authenticator app (Kind = Totp) ───────────────────────────────────────

    /// <summary>The TOTP secret wrapped with ASP.NET Core Data Protection, never stored raw.</summary>
    public string? ProtectedTotpSecret { get; set; }
    public bool? TotpIsConfirmed { get; set; }
    public DateTimeOffset? TotpEnrolledOn { get; set; }

    // ── External identity (Kind = ExternalIdentity) ───────────────────────────

    /// <summary>The token issuer, for example <c>https://accounts.google.com</c>.</summary>
    public string? Issuer { get; set; }

    /// <summary>The provider's stable subject identifier for this user. Never exposed by any API.</summary>
    public string? Subject { get; set; }

    /// <summary>The CloudLogin provider code ("Google", "Microsoft", ...) for display and routing.</summary>
    public string? ProviderCode { get; set; }

    /// <summary>
    /// The contact this external identity is attached to, by immutable
    /// <see cref="UserContact.ContactId"/>. Optional: a provider can be linked to an account
    /// without being tied to one of its contact points.
    /// </summary>
    public Guid? LinkedContactId { get; set; }

    /// <summary>
    /// The email address the provider reported for this identity, normalized. Shown on the
    /// account page so a person can tell two connections to the same provider apart — never used
    /// to resolve or link an identity, which is what <see cref="Issuer"/> and
    /// <see cref="Subject"/> are for.
    /// </summary>
    public string? ProviderEmail { get; set; }

    /// <summary>
    /// Whether the provider asserted that it had verified <see cref="ProviderEmail"/>. An
    /// unverified provider email never links anything and never satisfies a linking ceremony.
    /// </summary>
    public bool ProviderEmailIsVerified { get; set; }

    // ── Recovery artifact (Kind = Recovery) ───────────────────────────────────

    /// <summary>Purpose of a temporary recovery artifact, for example "password-reset".</summary>
    public string? RecoveryPurpose { get; set; }

    /// <summary>SHA-256 hash of the recovery secret. The raw value is never stored.</summary>
    public string? RecoverySecretHash { get; set; }

    // ── Expiry (recovery artifacts and other temporary credentials only) ─────

    public DateTimeOffset? ExpiresOn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    // ── Document id helpers ───────────────────────────────────────────────────

    /// <summary>
    /// The password credential's id: <c>password|{contactId}</c>. Keyed on the contact's
    /// immutable id, so the document an address's password lives in never moves when the address
    /// is corrected, re-cased, or re-normalized.
    /// </summary>
    public static string PasswordId(Guid contactId) => $"password|{contactId}";
    public static string PasskeyId(string credentialId) => $"passkey|{credentialId}";
    public const string TotpId = "totp";
    public static string ExternalIdentityId(string issuer, string subject) =>
        $"ext|{IdentityHashing.Hash($"{issuer}|{subject}")}";
    public static string RecoveryId(string purpose, Guid artifactId) => $"recovery|{purpose}|{artifactId}";
}
