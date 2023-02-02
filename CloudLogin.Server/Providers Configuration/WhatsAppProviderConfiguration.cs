using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Providers;

public class WhatsAppProviderConfiguration : ProviderConfiguration
{
	public string RequestUri { get; set; }
	public string Authorization { get; set; }
	public string Template { get; set; }
	public string Language { get; set; }

	public WhatsAppProviderConfiguration(IConfigurationSection configurationSection)
    {
        RequestUri = configurationSection["RequestUri"];
        Authorization = configurationSection["Authorization"];
        Template = configurationSection["Template"];
        Language = configurationSection["Language"];
        string label = configurationSection["Label"];

        Init("WhatsApp", label);

		HandlesPhoneNumber = true;
		IsCodeVerification = true;
	}
}
