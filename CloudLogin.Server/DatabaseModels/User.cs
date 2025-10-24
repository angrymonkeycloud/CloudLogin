namespace AngryMonkey.CloudLogin.Server;

public record UserInfo : BaseRecord
{
    // Ensure both Type and PartitionKey align with configuration
    public UserInfo() : base(GetEffectiveTypeValue(nameof(UserInfo)), GetEffectiveTypeValue(nameof(UserInfo))) { }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsLocked { get; set; } = false;
    public string? Username { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;
    public List<LoginInput> Inputs { get; set; } = [];

    // New optional profile fields to persist from external providers
    public string? ProfilePicture { get; set; }
    /// <summary>
    /// ISO3166-1 alpha-2 country code (e.g., "US", "GB").
    /// </summary>
    public string? Country { get; set; }
    /// <summary>
    /// Locale identifier from provider (e.g., "en-US", "fr-FR").
    /// </summary>
    public string? Locale { get; set; }
}