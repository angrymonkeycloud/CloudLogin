namespace AngryMonkey.CloudLogin;

public record LoginInput
{
    public InputFormat Format { get; set; } = InputFormat.Other;
    public string Input { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public string? PhoneNumberCountryCode { get; set; }
    public string? PhoneNumberCallingCode { get; set; }
    public List<LoginProvider> Providers { get; set; } = [];
}
