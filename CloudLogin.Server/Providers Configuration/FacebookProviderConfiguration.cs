namespace AngryMonkey.Cloud.Login.Providers;

public class FacebookProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public FacebookProviderConfiguration(string? label = null)
    {
        Init("Facebook", label);

        HandlesEmailAddress = true;
    }
}
