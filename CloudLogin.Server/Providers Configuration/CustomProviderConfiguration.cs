using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Providers;

public class CustomProviderConfiguration : ProviderConfiguration
{
    public CustomProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
    {
        string label = configurationSection["Label"];
        Init("custom", label);

        HandleUpdateOnly = handleUpdateOnly;
        HandlesEmailAddress = true;
        IsCodeVerification = true;
    }
}
