using AngryMonkey.CloudBlazor.Web;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Sever.Providers;

namespace AngryMonkey.CloudLogin.Server;

public class CloudLoginWebConfiguration
{
    /// <summary>
    /// Optional tuning for CloudLogin's seven-container storage model. CloudLogin has one storage
    /// model and creates it on startup; leaving this untouched uses secure defaults.
    /// </summary>
    public CloudLoginCoreConfiguration Core { get; set; } = new();

    /// <summary>
    /// The primary secret keying the identity index: at least 32 cryptographically random bytes,
    /// base64 or hex encoded. Every new identity row is written under this key.
    /// <para>
    /// Required whenever there is Azure storage to key. Bound from
    /// <c>CloudLogin:IdentityHmacSecret</c>, or from the portable environment variable
    /// <c>CloudLogin__IdentityHmacSecret</c> - the form that survives Linux App Service and
    /// containers, where a colon is not a legal variable name.
    /// </para>
    /// <para>
    /// Under Aspire the hosting integration generates and persists one automatically; elsewhere
    /// the deployment provides it. Changing it requires placing the previous value in
    /// <see cref="IdentityHmacFallbackSecrets"/> before the new primary is deployed.
    /// </para>
    /// <para>
    /// Never logged, never echoed in an error message, and never returned by any API.
    /// </para>
    /// </summary>
    public string? IdentityHmacSecret { get; set; }

    /// <summary>
    /// Old identity HMAC secrets accepted only for reads during a deliberate rotation. New writes
    /// always use <see cref="IdentityHmacSecret"/>; a fallback hit is safely re-keyed to the
    /// primary location. Keep all values in this one array setting and remove a fallback only after
    /// every identity has been migrated.
    /// <para>
    /// App settings use one JSON-array value named
    /// <c>CloudLogin:IdentityHmacFallbackSecrets</c> on Windows, or the portable environment name
    /// <c>CloudLogin__IdentityHmacFallbackSecrets</c>.
    /// </para>
    /// </summary>
    public List<string> IdentityHmacFallbackSecrets { get; set; } = [];

    /// <summary>Named sign-in profiles selectable via the login URL (<c>?profile=tv</c>).</summary>
    public SignInProfileConfiguration SignInProfiles { get; set; } = new();

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
    /// <summary>
    /// Adds or configures test-mode sign-in: pick a test user (or create one) and be signed in as
    /// them, with no credential check at all.
    /// </summary>
    /// <remarks>
    /// This is a deliberate bypass of authentication, so enable it only where that is acceptable —
    /// a development environment, or a local run. A host that decides per environment should pass
    /// the decision in rather than reading it from a settings file shared by every environment.
    /// </remarks>
    public CloudLoginWebConfiguration AddTestMode(bool isEnabled = true, string? label = null)
    {
        LoginTestProviders.TestModeConfiguration provider = new(isEnabled, label);

        int existingIndex = Providers.FindIndex(existing =>
            string.Equals(existing.Code, provider.Code, StringComparison.OrdinalIgnoreCase));

        if (existingIndex < 0)
            Providers.Add(provider);
        else
            Providers[existingIndex] = provider;

        return this;
    }

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
