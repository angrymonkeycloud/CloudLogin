namespace AngryMonkey.CloudLogin;

public record CloudLoginInput
{
    public CloudLoginInputFormat Format { get; set; } = CloudLoginInputFormat.Other;
    public string Input { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public string? PhoneNumberCountryCode { get; set; }
    public string? PhoneNumberCallingCode { get; set; }
    public List<CloudLoginProvider> Providers { get; set; } = [];
}
