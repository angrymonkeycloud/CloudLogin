﻿using AngryMonkey.CloudWeb;

namespace AngryMonkey.CloudLogin;

public class CloudLoginConfiguration
{
    public List<ProviderConfiguration> Providers { get; set; } = [];
    public string? BaseAddress { get; set; }
    public TimeSpan LoginDuration { get; set; } = new TimeSpan(180, 0, 0, 0); //10 months
    public List<Link> FooterLinks { get; set; } = [];
    public string? RedirectUri { get; set; }
    public CosmosConfiguration? Cosmos { get; set; }
    internal string? EmailMessageBody { get; set; }
    public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; }
    public CloudLoginEmailConfiguration? EmailConfiguration { get; set; }
    public required Action<CloudWebConfig> WebConfig { get; set; }
}
