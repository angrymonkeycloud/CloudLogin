using AngryMonkey.CloudLogin.Sever.Providers;
using AngryMonkey.CloudWeb;

namespace AngryMonkey.CloudLogin.Server;

public class CloudLoginWebConfiguration
{
    public List<ProviderConfiguration> Providers { get; set; } = [];
    public string? BaseAddress { get; set; }
    public TimeSpan LoginDuration { get; set; } = TimeSpan.FromDays(30);
    public List<Link> FooterLinks { get; set; } = [];
    public string? RedirectUri { get; set; }
    public CosmosConfiguration Cosmos { get; set; } = new();
    internal string? EmailMessageBody { get; set; }
    public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; }
    public CloudLoginEmailConfiguration? EmailConfiguration { get; set; }
    public Action<CloudWebConfig> WebConfig { get; set; } = static _ => { };
    public string? Logo { get; set; }
    public AzureStorageConfiguration? AzureStorage { get; set; } // Optional Azure Storage configuration

    /// <summary>
    /// Optional exact origins for websites hosted separately from CloudLogin.
    /// When empty, relative and same-origin redirects continue to work while
    /// cross-origin redirects are denied.
    /// </summary>
    public List<string> AllowedRedirectOrigins { get; set; } = [];

    /// <summary>
    /// Optional callback schemes for native applications. When empty, custom
    /// application-scheme redirects are denied.
    /// </summary>
    public List<string> AllowedMobileSchemes { get; set; } = [];
    public string CookieName { get; set; } = "__Host-CloudLogin";
    public string? CookieDomain { get; set; }
    public CloudLoginSecurityOptions Security { get; set; } = new();
    /// <summary>
    /// Enables the old code/QR flow that selects a user in browser code and then
    /// asks the server to create a session for that user. Keep disabled unless a
    /// legacy application still depends on it; new applications should use a
    /// server-validated authentication flow instead.
    /// </summary>
    public bool EnableLegacyClientManagedLogin { get; set; }

    /// <summary>Adds an exact HTTPS origin that may receive a login handoff.</summary>
    public CloudLoginWebConfiguration AllowWebsite(string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        AllowedRedirectOrigins.Add(origin.TrimEnd('/'));
        return this;
    }

    /// <summary>Adds a mobile callback scheme, for example <c>myapp</c>.</summary>
    public CloudLoginWebConfiguration AllowMobileApp(string callbackScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackScheme);
        AllowedMobileSchemes.Add(callbackScheme.Trim().ToLowerInvariant());
        return this;
    }
}
