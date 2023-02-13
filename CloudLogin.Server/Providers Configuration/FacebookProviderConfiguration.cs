using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Providers;

public class FacebookProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public FacebookProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
    {
        ClientId = configurationSection["ClientId"];
        ClientSecret = configurationSection["ClientSecret"];
        string label = configurationSection["Label"];

        Init("Facbook", label);
        HandleUpdateOnly = handleUpdateOnly;
        HandlesEmailAddress = true;
    }
}
