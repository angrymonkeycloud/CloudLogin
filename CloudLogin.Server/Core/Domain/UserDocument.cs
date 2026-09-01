namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>User lifecycle states. Durable, auditable, and independent of any API version.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum UserStates
{
    Active,
    Disabled,
    PendingDeletion,
    Deleted
}

/// <summary>
/// A user account in the <c>Users</c> container (partition key <c>/id</c>).
/// <para>
/// Holds profile, lifecycle, locale, timestamps, the security stamp, and the schema version.
/// Deliberately excluded: password hashes, passkeys, TOTP material, recovery artifacts, tokens,
/// and external provider subjects — those live in the <c>Credentials</c> container keyed by
/// <c>/userId</c>. Contact points (emails and phone numbers) are profile data and stay here, but
/// carry only provider <em>codes</em> for display; the provider's subject identifier never
/// appears in this document.
/// </para>
/// </summary>
public sealed class UserDocument : CloudLoginCoreDocument
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    public UserStates State { get; set; } = UserStates.Active;
    public bool IsLocked { get; set; }
    public bool IsTest { get; set; }
    public bool IsGlobalAdmin { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }
    public DateTimeOffset LastSignedInOn { get; set; }

    /// <summary>
    /// Rotates whenever the user's credentials or security-relevant state change. Authentication
    /// tickets carry it so a stale ticket can be rejected without a database of live sessions.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Contact points: every email address and phone number linked to the account.</summary>
    public List<UserContact> Contacts { get; set; } = [];

    public string? ProfilePicture { get; set; }
    public bool IsCustomProfilePicture { get; set; }
    public string? ProviderProfilePicture { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? Country { get; set; }

    /// <summary>IETF language tag, for example <c>en-US</c>.</summary>
    public string? Locale { get; set; }
}

/// <summary>
/// An email address or phone number linked to a user. Mirrors the legacy
/// <c>CloudLoginInput</c> minus everything secret: no password hashes and no provider subject
/// identifiers, only the provider codes needed to render "connected providers" in the UI.
/// </summary>
public sealed class UserContact
{
    /// <summary>
    /// The contact's immutable identity. Assigned once when the contact is first added and never
    /// changed afterwards — not when the address is re-cased, not when normalization changes, not
    /// when the person edits the display form.
    /// <para>
    /// Everything that points at a contact points at this: credential documents
    /// (<c>password|{contactId}</c>), identity index rows, and the account UI. Keying those on the
    /// address itself is what made a corrected email address orphan its own password, because the
    /// key moved while the credential stayed where it was.
    /// </para>
    /// </summary>
    public Guid ContactId { get; set; } = Guid.NewGuid();

    /// <summary>"EmailAddress" or "PhoneNumber" (matches <c>CloudLoginInputFormat</c> names).</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>The address or number exactly as entered/displayed.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The normalized form used for identity resolution (lowercased email / E.164 phone).</summary>
    public string NormalizedValue { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
    public bool IsVerified { get; set; }

    public string? PhoneNumberCountryCode { get; set; }
    public string? PhoneNumberCallingCode { get; set; }

    /// <summary>
    /// Provider codes attached to this contact (for example "Password", "Google"). Display and
    /// routing only — hashes and subjects live in the Credentials container.
    /// </summary>
    public List<string> ProviderCodes { get; set; } = [];
}
