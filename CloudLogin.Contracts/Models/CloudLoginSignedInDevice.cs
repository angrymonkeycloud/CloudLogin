namespace AngryMonkey.CloudLogin;

/// <summary>
/// A device the account is, or was, signed in on — one row in the account page's device list.
/// <para>
/// Everything here is descriptive: it comes from a client-supplied user agent and the remote
/// address observed at sign-in, so none of it identifies or authorizes anyone. Carries no token,
/// hash, or session secret.
/// </para>
/// </summary>
public record CloudLoginSignedInDevice
{
    /// <summary>Opaque id used to sign this one device out.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Human-readable summary, for example "Chrome on Windows".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Desktop, Mobile, Tablet, or Unknown — drives the icon shown beside the row.</summary>
    public string Type { get; set; } = "Unknown";

    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }

    /// <summary>Address seen at sign-in, and at the most recent token exchange.</summary>
    public string? SignedInFromIp { get; set; }
    public string? LastSeenIp { get; set; }

    /// <summary>UTC instants. The account page renders them in the viewer's own timezone.</summary>
    public DateTimeOffset SignedInOn { get; set; }

    public DateTimeOffset? LastSeenOn { get; set; }
    public DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>Whether this device can still act on the account.</summary>
    public bool IsActive { get; set; }

    /// <summary>Why an inactive device stopped, for example "TokenReuseDetected".</summary>
    public string? RevocationReason { get; set; }

    public DateTimeOffset? RevokedOn { get; set; }

    /// <summary>True for the device viewing the page, so it can be labelled "This device".</summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// The applications signed in to from this device (token audiences), so a person can tell a
    /// device that only opened the account page from one that is signed in to their products.
    /// </summary>
    public List<string> Audiences { get; set; } = [];
}
