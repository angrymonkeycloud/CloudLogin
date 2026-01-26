//using System.Text.Json;
//using System.Text.Json.Serialization;

//namespace AngryMonkey.CloudLogin;

//public record UserModel
//{
//    public Guid ID { get; set; }
//    public string? FirstName { get; set; }
//    public string? LastName { get; set; }
//    public string? DisplayName { get; set; }
//    public bool IsLocked { get; set; } = false;
//    public string? Username { get; set; }
//    public DateOnly? DateOfBirth { get; set; }
//    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
//    public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;
//    public List<string> Inputs { get; set; } = [];

//    // New optional profile fields populated from external providers (best-effort)
//    // Never overwritten on updates; only set when currently null.
//    public string? ProfilePicture { get; set; }
//    /// <summary>
//    /// ISO3166-1 alpha-2 country code (e.g., "US", "GB").
//    /// </summary>
//    public string? Country { get; set; }
//    /// <summary>
//    /// Locale identifier from provider (e.g., "en-US", "fr-FR").
//    /// </summary>
//    public string? Locale { get; set; }
//    public string[] EmailAddresses { get; set; } = [];
//    public required string PrimaryEmailAddress { get; set; }
//}