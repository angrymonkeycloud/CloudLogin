namespace AngryMonkey.Cloud.Login.Providers;

public class CustomProviderConfiguration : ProviderConfiguration
{
    public CustomProviderConfiguration(string? label = null) : base("custom", label)
    {
        HandlesEmailAddress = true;
        IsCodeVerification = true;
    }
}
