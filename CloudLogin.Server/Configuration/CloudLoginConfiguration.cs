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
    public List<string> AllowedRedirectOrigins { get; set; } = [];
    public List<string> AllowedMobileSchemes { get; set; } = [];
    public string CookieName { get; set; } = "CloudLogin";
    public string? CookieDomain { get; set; }
    /// <summary>
    /// Enables the old code/QR flow that selects a user in browser code and then
    /// asks the server to create a session for that user. Keep disabled unless a
    /// legacy application still depends on it; new applications should use a
    /// server-validated authentication flow instead.
    /// </summary>
    public bool EnableLegacyClientManagedLogin { get; set; }
}
