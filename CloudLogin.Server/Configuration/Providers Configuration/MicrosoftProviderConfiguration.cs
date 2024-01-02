using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Providers;

public class MicrosoftProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	//public MicrosoftProviderConfiguration(string? label = null) : base("Microsoft", label)
	//{
	//	HandlesEmailAddress = true;
	//}

	public MicrosoftProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
	{
        ClientId = configurationSection["ClientId"];
        ClientSecret = configurationSection["ClientSecret"];
        string label = configurationSection["Label"];

        Init("Microsoft", label);
		HandleUpdateOnly = handleUpdateOnly;
		HandlesEmailAddress = true;
	}
}
