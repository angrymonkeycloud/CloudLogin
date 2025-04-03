using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Server;

public class GoogleProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public GoogleProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
    {
        ClientId = configurationSection["ClientId"];
        ClientSecret = configurationSection["ClientSecret"];
        string label = configurationSection["Label"];

        Init("Google", label);
        HandleUpdateOnly = handleUpdateOnly;
        HandlesEmailAddress = true;
	}
}
