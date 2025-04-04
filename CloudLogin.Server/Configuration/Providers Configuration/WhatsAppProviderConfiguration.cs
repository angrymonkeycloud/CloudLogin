using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Sever.Providers;

public class WhatsAppProviderConfiguration : ProviderConfiguration
{
	public string RequestUri { get; set; }
	public string Authorization { get; set; }
	public string Template { get; set; }
	public string Language { get; set; }

	public WhatsAppProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
    {
        RequestUri = configurationSection["RequestUri"];
        Authorization = configurationSection["Authorization"];
        Template = configurationSection["Template"];
        Language = configurationSection["Language"];
        string label = configurationSection["Label"];

        Init("WhatsApp", label);
		HandleUpdateOnly = handleUpdateOnly;
		HandlesPhoneNumber = true;
		InputRequired = true;
		IsCodeVerification = true;
	}
}
