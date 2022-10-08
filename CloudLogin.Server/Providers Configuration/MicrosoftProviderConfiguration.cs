namespace AngryMonkey.Cloud.Login.DataContract;

public class MicrosoftProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public MicrosoftProviderConfiguration(string? label = null) : base("Microsoft", label)
	{
		HandlesEmailAddress = true;
	}
}
