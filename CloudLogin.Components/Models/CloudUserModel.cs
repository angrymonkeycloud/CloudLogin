namespace AngryMonkey.CloudLogin.Models;

public class CloudUserModel
{
    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsLocked { get; set; } = false;
    public bool IsTest { get; set; } = false;
    public bool IsGlobalAdmin { get; set; } = false;
    public string? Username { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;
    public List<CloudLoginInputModel> Inputs { get; set; } = [];
    public string? ProfilePicture { get; set; }
    public bool IsCustomProfilePicture { get; set; } = false;
    public string? ProviderProfilePicture { get; set; }
    public string? Country { get; set; }
    public string? Locale { get; set; }

    public List<CloudLoginInputModel> EmailAddresses => [.. Inputs.Where(key => key.Format == CloudLoginInputFormat.EmailAddress)];
    public List<CloudLoginInputModel> PhoneNumbers => [.. Inputs.Where(key => key.Format == CloudLoginInputFormat.PhoneNumber)];
    public CloudLoginInputModel? PrimaryEmailAddress => EmailAddresses.FirstOrDefault(key => key.IsPrimary);
    public CloudLoginInputModel? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault(key => key.IsPrimary);
    public List<string> Providers => [.. Inputs.SelectMany(input => input.Providers).Select(key => key.Code).Distinct()];
}

public static class CloudUserModelExtensions
{
    public static CloudUserModel ToModel(this CloudUser source) => new()
    {
        Id = source.Id,
        FirstName = source.FirstName,
        LastName = source.LastName,
        DisplayName = source.DisplayName,
        IsLocked = source.IsLocked,
        IsTest = source.IsTest,
        IsGlobalAdmin = source.IsGlobalAdmin,
        Username = source.Username,
        DateOfBirth = source.DateOfBirth,
        CreatedOn = source.CreatedOn,
        LastSignedIn = source.LastSignedIn,
        Inputs = [.. source.Inputs.Select(input => input.ToModel())],
        ProfilePicture = source.ProfilePicture,
        IsCustomProfilePicture = source.IsCustomProfilePicture,
        ProviderProfilePicture = source.ProviderProfilePicture,
        Country = source.Country,
        Locale = source.Locale
    };

    /// <summary>Builds a payload from a model that carries every field itself (nothing on <see cref="CloudUser"/> is system-owned beyond what the model already tracks).</summary>
    public static CloudUser ToContract(this CloudUserModel model) => model.ToContract(new CloudUser());

    public static CloudUser ToContract(this CloudUserModel model, CloudUser original) => original with
    {
        Id = model.Id,
        FirstName = model.FirstName,
        LastName = model.LastName,
        DisplayName = model.DisplayName,
        IsLocked = model.IsLocked,
        IsTest = model.IsTest,
        IsGlobalAdmin = model.IsGlobalAdmin,
        Username = model.Username,
        DateOfBirth = model.DateOfBirth,
        CreatedOn = model.CreatedOn,
        LastSignedIn = model.LastSignedIn,
        Inputs = [.. model.Inputs.Select(input => input.ToContract())],
        ProfilePicture = model.ProfilePicture,
        IsCustomProfilePicture = model.IsCustomProfilePicture,
        ProviderProfilePicture = model.ProviderProfilePicture,
        Country = model.Country,
        Locale = model.Locale
    };
}
