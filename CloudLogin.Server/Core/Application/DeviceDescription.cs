namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>Broad device categories, derived from the user agent.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DeviceTypes
{
    Unknown,
    Desktop,
    Mobile,
    Tablet
}

/// <summary>
/// What a user agent says about the device behind a session.
/// <para>
/// Deliberately coarse: browser, operating system and a broad type are enough to let someone
/// recognise their own devices in a sign-in list, which is the only purpose this serves. It is
/// never used for authorization — a user agent is client-supplied text and trivially forged, so
/// treating it as identifying would be a vulnerability, not a feature.
/// </para>
/// </summary>
public sealed record DeviceDescription
{
    /// <summary>Human-readable summary, for example "Chrome on Windows".</summary>
    public required string Name { get; init; }

    public required DeviceTypes Type { get; init; }

    public string? Browser { get; init; }

    public string? OperatingSystem { get; init; }

    /// <summary>The description used when no user agent was recorded.</summary>
    public static DeviceDescription Unknown { get; } = new()
    {
        Name = "Unknown device",
        Type = DeviceTypes.Unknown
    };

    /// <summary>
    /// Parses a user agent. Never throws and never returns null: an unrecognised agent yields
    /// <see cref="Unknown"/> rather than failing a sign-in.
    /// </summary>
    public static DeviceDescription Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return Unknown;

        string browser =
            userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera" :
            userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
            "Browser";

        string operatingSystem =
            userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone" :
            userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad" :
            userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
            "Unknown device";

        return new DeviceDescription
        {
            Name = $"{browser} on {operatingSystem}",
            Type = DetectType(userAgent),
            Browser = browser,
            OperatingSystem = operatingSystem
        };
    }

    /// <summary>
    /// Tablet is checked before mobile: an iPad reports "Safari", and Android tablets are exactly
    /// the Android agents that omit the "Mobile" token, so testing mobile first would miscategorise
    /// every one of them.
    /// </summary>
    private static DeviceTypes DetectType(string userAgent)
    {
        bool android = userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase);

        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
            || (android && !userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)))
            return DeviceTypes.Tablet;

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase)
            || android)
            return DeviceTypes.Mobile;

        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
            return DeviceTypes.Desktop;

        return DeviceTypes.Unknown;
    }
}
