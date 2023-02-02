using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Providers;

public class CustomProviderConfiguration : ProviderConfiguration
{
    public CustomProviderConfiguration(IConfigurationSection configurationSection)
    {
        string label = configurationSection["Label"];
        Init("custom", label);

        HandlesEmailAddress = true;
        IsCodeVerification = true;
    }
}
