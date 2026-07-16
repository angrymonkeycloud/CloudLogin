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
    /// Enables the deprecated flow that creates and validates verification codes
    /// in browser code. This is insecure and must remain disabled in production.
    /// </summary>
    public bool EnableLegacyClientVerificationCodes { get; set; }
}
