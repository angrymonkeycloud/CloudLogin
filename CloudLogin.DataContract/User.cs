using System.Text.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

public record User
{
    public Guid ID { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsLocked { get; set; } = false;
    public string? Username { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? PasswordHash { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;
    public List<LoginInput> Inputs { get; set; } = [];

    // Ignore

    [JsonIgnore] public List<LoginInput> EmailAddresses => Inputs.Where(key => key.Format == InputFormat.EmailAddress).ToList();
    [JsonIgnore] public List<LoginInput> PhoneNumbers => Inputs.Where(key => key.Format == InputFormat.PhoneNumber).ToList();
    [JsonIgnore] public LoginInput? PrimaryEmailAddress => EmailAddresses?.FirstOrDefault(key => key.IsPrimary);
    [JsonIgnore] public LoginInput? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault(key => key.IsPrimary);
    [JsonIgnore] public List<string> Providers => Inputs.SelectMany(input => input.Providers).Select(key => key.Code).Distinct().ToList();

    public static User? Parse(string? decoded)
    {
        if (string.IsNullOrEmpty(decoded))
            return null;

        return JsonSerializer.Deserialize<User>(decoded);
    }
}