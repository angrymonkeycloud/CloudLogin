using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// One recorded sign-in, shown in the account page's security timeline.
/// <para>
/// Coordinates are stored exactly as the client reported them and are never resolved to a
/// place name — CloudLogin performs no geocoding lookup, so no mapping API key is required.
/// The account page turns a coordinate into an external map link instead.
/// </para>
/// </summary>
public record LoginHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>When the sign-in completed (UTC).</summary>
    public DateTimeOffset SignedInOn { get; set; }

    /// <summary>Provider code used for this sign-in (e.g. "Google", "Password", "Code").</summary>
    public string? Provider { get; set; }

    /// <summary>Remote address observed at sign-in, if the host recorded one.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Raw user agent string, used to describe the device.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Short human-readable device description derived from the user agent.</summary>
    public string? Device { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [JsonIgnore]
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;
}

/// <summary>
/// The per-user login-history document. Persisted as a single JSON blob per user rather than
/// on the user record, so an active account's sign-in log never bloats the document that gets
/// read on every request.
/// </summary>
public record LoginHistoryDocument
{
    public Guid UserId { get; set; }
    public List<LoginHistoryEntry> Entries { get; set; } = [];
}

/// <summary>A registered WebAuthn credential (passkey, device PIN, fingerprint, or face unlock).</summary>
public record UserPasskey
{
    /// <summary>Base64url-encoded credential id.</summary>
    public string CredentialId { get; set; } = string.Empty;

    public byte[] PublicKey { get; set; } = [];

    /// <summary>
    /// Authenticator signature counter. A counter that fails to advance can indicate a cloned
    /// authenticator, so it is persisted and checked on every assertion.
    /// </summary>
    public uint SignCount { get; set; }

    /// <summary>Name the user gave this credential, e.g. "MacBook Touch ID".</summary>
    public string? Name { get; set; }

    public Guid AaGuid { get; set; }
    public List<string> Transports { get; set; } = [];
    public bool IsBackedUp { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? LastUsedOn { get; set; }
}

/// <summary>A TOTP authenticator-app enrollment.</summary>
public record UserAuthenticatorApp
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

/// <summary>
/// Per-user security credentials, stored separately from the user record so that secrets
/// (TOTP keys, passkey material) are never part of the <see cref="UserModel"/> that the
/// browser receives.
/// </summary>
public record UserSecurityDocument
{
    public Guid UserId { get; set; }
    public List<UserPasskey> Passkeys { get; set; } = [];
    public UserAuthenticatorApp? Authenticator { get; set; }
}

/// <summary>
/// Safe projection of a user's security state for display. Contains no secret material:
/// no TOTP key, no passkey public keys.
/// </summary>
public record SecurityOverview
{
    /// <summary>True when a password credential exists and can be changed rather than created.</summary>
    public bool HasPassword { get; set; }

    /// <summary>True when the host has the password provider configured at all.</summary>
    public bool PasswordProviderConfigured { get; set; }

    public bool HasAuthenticatorApp { get; set; }
    public DateTimeOffset? AuthenticatorEnrolledOn { get; set; }

    public List<PasskeySummary> Passkeys { get; set; } = [];

    /// <summary>Providers linked to this account, with the input they're linked through.</summary>
    public List<ConnectedProvider> ConnectedProviders { get; set; } = [];

    /// <summary>Providers the host supports that this account hasn't linked yet.</summary>
    public List<ProviderDefinition> AvailableProviders { get; set; } = [];
}

/// <summary>Display-safe view of a registered passkey.</summary>
public record PasskeySummary
{
    public string CredentialId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? LastUsedOn { get; set; }
    public bool IsBackedUp { get; set; }
}

/// <summary>A provider currently linked to the account.</summary>
public record ConnectedProvider
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

/// <summary>
/// Material handed to the user exactly once, when enrolling an authenticator app. After the
/// enrollment is confirmed the secret is never returned again.
/// </summary>
public record AuthenticatorEnrollmentModel
{
    /// <summary>Base32 secret, for manual entry when a QR code can't be scanned.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The otpauth:// URI to render as a QR code.</summary>
    public string ProvisioningUri { get; set; } = string.Empty;
}

/// <summary>Request to set or change the signed-in user's password.</summary>
public sealed record ChangePasswordRequest
{
    /// <summary>Existing password. Required when the account already has one.</summary>
    public string? CurrentPassword { get; init; }

    public required string NewPassword { get; init; }
}
