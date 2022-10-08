namespace AngryMonkey.Cloud.Login.DataContract;

public class GoogleProviderConfiguration : ProviderConfiguration
{
	public string ClientId { get; init; } = string.Empty;
	public string ClientSecret { get; init; } = string.Empty;

	public GoogleProviderConfiguration(string? label = null) : base("Google", label)
	{
		HandlesEmailAddress = true;
	}
}
