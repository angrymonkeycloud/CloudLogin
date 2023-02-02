﻿namespace AngryMonkey.Cloud.Login.Providers;

public class WhatsAppProviderConfiguration : ProviderConfiguration
{
	public string RequestUri { get; set; }
	public string Authorization { get; set; }
	public string Template { get; set; }
	public string Language { get; set; }

	public WhatsAppProviderConfiguration(string? label = null)
	{
		Init("WhatsApp", label);

		HandlesPhoneNumber = true;
		IsCodeVerification = true;
	}
}
