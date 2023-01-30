namespace AngryMonkey.Cloud.Login.Providers;

public class TwitterProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public TwitterProviderConfiguration(string? label = null) : base("Twitter", label)
	{
		HandlesEmailAddress = true;
	}
}
