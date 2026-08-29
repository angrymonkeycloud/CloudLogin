using AngryMonkey.CloudBlazor.Web;
using AngryMonkey.CloudLogin.Sever.Providers;

namespace AngryMonkey.CloudLogin.Server;

public class CloudLoginWebConfiguration
{
    public List<ProviderConfiguration> Providers { get; set; } = [];
    public string? Title { get; set; }
    public string? BaseAddress { get; set; }
    public TimeSpan LoginDuration { get; set; } = TimeSpan.FromDays(30);
    public List<CloudLoginLink> FooterLinks { get; set; } = [];
    public string? RedirectUri { get; set; }
    public CosmosConfiguration Cosmos { get; set; } = new();
    internal string? EmailMessageBody { get; set; }
    public Func<CloudLoginSendCodeValue, Task>? EmailSendCodeRequest { get; set; }
    public CloudLoginEmailConfiguration? EmailConfiguration { get; set; }
    public Action<CloudWebConfig> WebConfig { get; set; } = static _ => { };
    public string? Logo { get; set; }
    public AzureStorageConfiguration? AzureStorage { get; set; } // Optional Azure Storage configuration

    /// <summary>
    /// Optional workspace feature. When omitted, the workspace account page and API are disabled.
    /// </summary>
    public WorkspaceConfiguration? Workspace { get; set; }

    /// <summary>
    /// The primary/accent color used across the login and account UI, as a hex string
    /// (e.g. "#0078D4" or "#06C"). Defaults to blue.
    /// </summary>
    public string PrimaryColor { get; set; } = "#0078D4";

    /// <summary>
    /// Optional exact origins for websites hosted separately from CloudLogin.
    /// When empty, no website allowlist has been configured, so every
    /// cross-origin redirect target is allowed — there is nothing to restrict
    /// against. Add entries here to restrict handoffs to only those origins.
    /// </summary>
    public List<string> AllowedRedirectOrigins { get; set; } = [];

    /// <summary>
    /// Optional callback schemes for native applications. When empty, no app
    /// allowlist has been configured, so every custom application-scheme
    /// redirect is allowed. Add entries here to restrict handoffs to only
    /// those schemes.
    /// </summary>
    public List<string> AllowedMobileSchemes { get; set; } = [];
    public string CookieName { get; set; } = "__Host-CloudLogin";
    public string? CookieDomain { get; set; }
    public CloudLoginSecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Shared secrets accepted by the "ServiceKey" authentication scheme, used by trusted
    /// backend services (not browsers) to call the service-to-service lookup endpoints
    /// (e.g. <c>CloudLogin/Service/Workspaces/{id}</c>). Empty by default — those
    /// endpoints reject every request until at least one key is configured. Never expose
    /// these to a browser; store them via user secrets / a secret manager on both sides.
    /// </summary>

    public List<string> ServiceKeys { get; set; } = [];

    /// <summary>Application-neutral signed webhook registrations.</summary>
    public List<CloudLoginWebhookRegistration> Webhooks { get; set; } = [];
    /// <summary>Adds or configures Microsoft sign-in.</summary>
    public CloudLoginWebConfiguration AddMicrosoft(
        Action<LoginProviders.MicrosoftProviderConfiguration>? configure = null)
    {
        LoginProviders.MicrosoftProviderConfiguration provider = new();
        configure?.Invoke(provider);

        int existingIndex = Providers.FindIndex(existing =>
            string.Equals(existing.Code, provider.Code, StringComparison.OrdinalIgnoreCase));

        if (existingIndex < 0)
            Providers.Add(provider);
        else
            Providers[existingIndex] = provider;

        return this;
    }

    /// Enables the old code/QR flow that selects a user in browser code and then
    /// <summary>
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
