namespace AngryMonkey.CloudLogin.V3;

/// <summary>
/// One device the account is, or was, signed in on. Descriptive only — everything here comes
/// from a client-supplied user agent and the observed remote address, so none of it identifies
/// or authorizes anyone. Carries no token, no hash and no session secret.
/// </summary>
public sealed record V3SignedInDeviceResponse
{
    /// <summary>Opaque id used to sign this one device out.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Human-readable summary, for example "Chrome on Windows".</summary>
    public required string Name { get; init; }

    /// <summary>Desktop, Mobile, Tablet, or Unknown.</summary>
    public required string Type { get; init; }

    public string? Browser { get; init; }
    public string? OperatingSystem { get; init; }

    public string? SignedInFromIp { get; init; }
    public string? LastSeenIp { get; init; }

    /// <summary>UTC instants; the client renders them in the viewer's own timezone.</summary>
    public required DateTimeOffset SignedInOn { get; init; }

    public DateTimeOffset? LastSeenOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }

    /// <summary>Whether this device can still act on the account.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Why an inactive device stopped, for example "TokenReuseDetected".</summary>
    public string? RevocationReason { get; init; }

    public DateTimeOffset? RevokedOn { get; init; }

    /// <summary>True for the device making this request, so it can be labelled "This device".</summary>
    public bool IsCurrent { get; init; }

    /// <summary>The applications signed in to from this device (token audiences).</summary>
    public List<string> Audiences { get; init; } = [];
}

/// <summary>The result of signing every other device out.</summary>
public sealed record V3RevokeOtherDevicesResponse
{
    /// <summary>How many devices were signed out. Zero is a success, not an error.</summary>
    public required int RevokedCount { get; init; }
}
