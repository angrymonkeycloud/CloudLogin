namespace AngryMonkey.Cloud.Login.DataContract;

public class WhatsAppProviderConfiguration : ProviderConfiguration
{
	public string RequestUri { get; set; }
	public string Authorization { get; set; }
	public string Template { get; set; }
	public string Language { get; set; }

	public WhatsAppProviderConfiguration(string? label = null) : base("whatsapp", label)
	{
		HandlesPhoneNumber = true;
		IsCodeVerification = true;
	}
}
