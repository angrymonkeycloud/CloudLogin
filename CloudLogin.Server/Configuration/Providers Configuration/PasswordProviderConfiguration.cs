using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Sever.Providers;

public class PasswordProviderConfiguration : ProviderConfiguration
{
    public PasswordProviderConfiguration(IConfigurationSection configurationSection)
    {
        string? label = configurationSection["Label"] ?? "Email";
        Init("password", label);
        HandleUpdateOnly = true;
        HandlesEmailAddress = true;
        InputRequired = true;
        IsCodeVerification = false;
    }
}
