namespace AngryMonkey.Cloud.Login.Providers;

public class GoogleProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public GoogleProviderConfiguration(string? label = null) 
    {
        Init("Google", label);

        HandlesEmailAddress = true;
	}
}
