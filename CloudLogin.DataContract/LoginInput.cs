namespace AngryMonkey.CloudLogin;

public record LoginInput
{
    public string Input { get; set; } = string.Empty;
    public InputFormat Format { get; set; } = InputFormat.Other;
    public string? PhoneNumberCountryCode { get; set; }
    public string? PhoneNumberCallingCode { get; set; }
    public List<LoginProvider> Providers { get; set; } = [];
    public bool IsPrimary { get; set; } = false;
}
