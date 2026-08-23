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
public record CloudLoginHistoryEntry
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
