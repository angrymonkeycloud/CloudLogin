namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginHistoryEntryModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset SignedInOn { get; set; }
    public string? Provider { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Device { get; set; }
    public string? MapImageUrl { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool HasCoordinates => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

public static class CloudLoginHistoryEntryModelExtensions
{
    public static CloudLoginHistoryEntryModel ToModel(this CloudLoginHistoryEntry source) => new()
    {
        Id = source.Id,
        MapImageUrl = source.MapImageUrl,
        SignedInOn = source.SignedInOn,
        Provider = source.Provider,
        IpAddress = source.IpAddress,
        UserAgent = source.UserAgent,
        Device = source.Device,
        Latitude = source.Latitude,
        Longitude = source.Longitude
    };
}
