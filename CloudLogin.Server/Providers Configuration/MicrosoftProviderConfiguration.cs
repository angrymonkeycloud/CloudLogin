using Microsoft.Extensions.Configuration;

namespace AngryMonkey.Cloud.Login.Providers;

public class MicrosoftProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	//public MicrosoftProviderConfiguration(string? label = null) : base("Microsoft", label)
	//{
	//	HandlesEmailAddress = true;
	//}

	public MicrosoftProviderConfiguration(IConfigurationSection configurationSection)
	{
        ClientId = configurationSection["ClientId"];
        ClientSecret = configurationSection["ClientSecret"];
        string label = configurationSection["Label"];

        Init("Microsoft", label);
		HandlesEmailAddress = true;
	}
}
