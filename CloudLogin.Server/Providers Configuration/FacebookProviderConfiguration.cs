namespace AngryMonkey.Cloud.Login.Providers;

public class FacebookProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public FacebookProviderConfiguration(string? label = null) : base("Facebook", label)
	{
		HandlesEmailAddress = true;

    }
}
