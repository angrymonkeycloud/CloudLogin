using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Providers;

public class TwitterProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public TwitterProviderConfiguration(IConfigurationSection configurationSection)
    {
        ClientId = configurationSection["ClientId"];
        ClientSecret = configurationSection["ClientSecret"];
        string label = configurationSection["Label"];

        Init("Twitter", label);
        HandlesEmailAddress = true;
	}
}
