namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginHistoryEntryModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset SignedInOn { get; set; }
    public string? Provider { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Device { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;
}

public static class CloudLoginHistoryEntryModelExtensions
{
    public static CloudLoginHistoryEntryModel ToModel(this CloudLoginHistoryEntry source) => new()
    {
        Id = source.ID,
        SignedInOn = source.SignedInOn,
        Provider = source.Provider,
        IpAddress = source.IpAddress,
        UserAgent = source.UserAgent,
        Device = source.Device,
        Latitude = source.Latitude,
        Longitude = source.Longitude
    };
}
