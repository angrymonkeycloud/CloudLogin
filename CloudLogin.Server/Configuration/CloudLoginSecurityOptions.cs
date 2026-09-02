namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Security controls applied by CloudLogin. Defaults are suitable for an
/// internet-facing production deployment and normally require no changes.
/// </summary>
public sealed class CloudLoginSecurityOptions
{
    public const int MinimumPbkdf2Iterations = 600_000;

    /// <summary>Reject non-HTTPS public origins and emit Secure cookies.</summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>Idle lifetime of the authority authentication ticket.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Minimum accepted length for newly created passwords.</summary>
    public int MinimumPasswordLength { get; set; } = 12;

    /// <summary>Maximum accepted length, limiting password-hashing denial of service.</summary>
    public int MaximumPasswordLength { get; set; } = 128;

    /// <summary>PBKDF2-HMAC-SHA256 work factor for new and upgraded password hashes.</summary>
    public int PasswordHashIterations { get; set; } = MinimumPbkdf2Iterations;

    /// <summary>
    /// Application-specific compromised/common passwords to reject. Add your
    /// organization's breached-password feed values during startup.
    /// </summary>
    public ISet<string> PasswordBlocklist { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "password123",
        "123456789012",
        "qwerty123456",
        "letmein123456"
    };

    /// <summary>Authentication attempts allowed per client during one window.</summary>
    public int AuthenticationPermitLimit { get; set; } = 10;

    public TimeSpan AuthenticationWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum accepted profile-image payload.</summary>
    public int MaximumProfileImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Exact remote hosts from which provider profile images may be copied into
    /// configured storage. Empty means provider images are referenced, not downloaded.
    /// </summary>
    public ISet<string> AllowedProfileImageHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Enables the deprecated endpoints that let browser code choose a verification code and have
    /// the server mail it. The modern flow needs none of them - the server issues and checks the
    /// code itself - so this stays off, and cannot be turned on outside Development.
    /// </summary>
    public bool EnableLegacyClientVerificationCodes { get; set; }

    /// <summary>Digits in a server-issued verification code.</summary>
    public int VerificationCodeLength { get; set; } = 6;

    /// <summary>How long a verification code is accepted after it is issued.</summary>
    public TimeSpan VerificationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Wrong codes accepted against one challenge before it is dead and a new code must be sent.
    /// This is what keeps a short numeric code out of reach of guessing: with the default six
    /// digits, five attempts leave a one-in-two-hundred-thousand chance per challenge.
    /// </summary>
    public int MaximumVerificationAttempts { get; set; } = 5;

    /// <summary>
    /// Most recent sign-in records kept per user. Older records are pruned on write so the
    /// per-user history blob stays a bounded size.
    /// </summary>
    public int LoginHistoryMaximumEntries { get; set; } = 100;

    /// <summary>
    /// How long a sign-in record is retained. Records older than this are pruned on write
    /// even when the account is well under <see cref="LoginHistoryMaximumEntries"/>.
    /// </summary>
    public TimeSpan LoginHistoryRetention { get; set; } = TimeSpan.FromDays(180);

    /// <summary>
    /// Relying Party ID for WebAuthn (passkeys). Must be the site's registrable domain —
    /// e.g. "example.com" for https://login.example.com. Leave null to derive it from the
    /// request host, which is correct for single-host deployments.
    /// </summary>
    public string? WebAuthnRelyingPartyId { get; set; }

    /// <summary>Display name shown by the authenticator during passkey registration.</summary>
    public string WebAuthnRelyingPartyName { get; set; } = "CloudLogin";

    /// <summary>
    /// Additional origins accepted during WebAuthn ceremonies. The request's own origin is
    /// always accepted; add entries here only for extra hosts that share the RP ID.
    /// </summary>
    public ISet<string> WebAuthnAllowedOrigins { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
