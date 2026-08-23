namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginInputModel
{
    public CloudLoginInputFormat Format { get; set; } = CloudLoginInputFormat.Other;
    public string Input { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public string? PhoneNumberCountryCode { get; set; }
    public string? PhoneNumberCallingCode { get; set; }
    public List<CloudLoginProvider> Providers { get; set; } = [];
}

public static class CloudLoginInputModelExtensions
{
    public static CloudLoginInputModel ToModel(this CloudLoginInput source) => new()
    {
        Format = source.Format,
        Input = source.Input,
        IsPrimary = source.IsPrimary,
        PhoneNumberCountryCode = source.PhoneNumberCountryCode,
        PhoneNumberCallingCode = source.PhoneNumberCallingCode,
        Providers = [.. source.Providers]
    };

    public static CloudLoginInput ToContract(this CloudLoginInputModel model) => new()
    {
        Format = model.Format,
        Input = model.Input,
        IsPrimary = model.IsPrimary,
        PhoneNumberCountryCode = model.PhoneNumberCountryCode,
        PhoneNumberCallingCode = model.PhoneNumberCallingCode,
        Providers = [.. model.Providers]
    };
}
