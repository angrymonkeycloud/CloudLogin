using AngryMonkey.CloudLogin.Sever.Providers;
using AngryMonkey.CloudWeb;

namespace AngryMonkey.CloudLogin.Server;

public class CloudLoginWebConfiguration
{
    public List<ProviderConfiguration> Providers { get; set; } = [];
    public string? BaseAddress { get; set; }
    public TimeSpan LoginDuration { get; set; } = new TimeSpan(180, 0, 0, 0); //10 months
    public List<Link> FooterLinks { get; set; } = [];
    public string? RedirectUri { get; set; }
    public CosmosConfiguration Cosmos { get; set; } = new();
    internal string? EmailMessageBody { get; set; }
    public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; }
    public CloudLoginEmailConfiguration? EmailConfiguration { get; set; }
    public required Action<CloudWebConfig> WebConfig { get; set; }
    public string? Logo { get; set; }
    public AzureStorageConfiguration? AzureStorage { get; set; } // Optional Azure Storage configuration
}
