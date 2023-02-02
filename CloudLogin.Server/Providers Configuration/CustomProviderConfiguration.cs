namespace AngryMonkey.Cloud.Login.Providers;

public class CustomProviderConfiguration : ProviderConfiguration
{
    public CustomProviderConfiguration(string? label = null) 
    {
        Init("custom", label);

        HandlesEmailAddress = true;
        IsCodeVerification = true;
    }
}
