using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Aspire;

/// <summary>
/// Builds CloudLogin's runtime configuration from the same configuration hierarchy projected by
/// the Aspire hosting integration.
/// </summary>
public static class CloudLoginConfigurationExtensions
{
    /// <summary>
    /// Applies optional project-owned defaults, then lets host configuration override the values it
    /// explicitly supplies.
    /// </summary>
    /// <param name="builder">The CloudLogin host application builder.</param>
    /// <param name="configure">
    /// Optional project-level defaults applied before host-projected values.
    /// </param>
    /// <returns>The complete configuration consumed by CloudLogin Web.</returns>
    public static CloudLoginWebConfiguration ReadCloudLoginConfiguration(
        this IHostApplicationBuilder builder,
        Action<CloudLoginWebConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        CloudLoginWebConfiguration configuration = new();
        configure?.Invoke(configuration);

        builder.Configuration.GetSection("CloudLogin").Bind(configuration);

        // App Service and container settings are scalar values. Keep rotation fallbacks in one
        // secret setting by accepting a JSON array instead of requiring __0, __1, ... variables.
        // A normal appsettings.json array still binds through the line above.
        IConfigurationSection fallbackSection =
            builder.Configuration.GetSection("CloudLogin:IdentityHmacFallbackSecrets");

        if (fallbackSection.Value is string fallbackJson)
        {
            try
            {
                configuration.IdentityHmacFallbackSecrets =
                    JsonSerializer.Deserialize<List<string>>(fallbackJson) ?? [];
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    "CloudLogin:IdentityHmacFallbackSecrets must be one JSON array of base64 or hex secrets.",
                    exception);
            }
        }

        configuration.BindAspireResources(builder);
        MergeProviders(
            configuration.Providers,
            BuildProviders(
                builder.Configuration,
                configuration.Providers));

        return configuration;
    }

    private static void MergeProviders(
        List<ProviderConfiguration> projectProviders,
        List<ProviderConfiguration> hostProviders)
    {
        foreach (ProviderConfiguration hostProvider in hostProviders)
        {
            int index = projectProviders.FindIndex(provider =>
                string.Equals(provider.Code, hostProvider.Code, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                projectProviders.Add(hostProvider);
            else
                projectProviders[index] = hostProvider;
        }
    }

    private static List<ProviderConfiguration> BuildProviders(
        IConfiguration configuration,
        IReadOnlyList<ProviderConfiguration> projectProviders)
    {
        List<ProviderConfiguration> providers = [];

        AddWhenConfigured("Password", "password", section => new LoginProviders.PasswordProviderConfiguration(section));
        AddWhenConfigured("Code", "code", section => new LoginProviders.CodeProviderConfiguration(section));
        AddWhenConfigured("Microsoft", "Microsoft", section => new LoginProviders.MicrosoftProviderConfiguration(section));
        AddWhenConfigured("Google", "Google", section => new LoginProviders.GoogleProviderConfiguration(section));
        AddWhenConfigured("Facebook", "Facebook", section => new LoginProviders.FacebookProviderConfiguration(section));
        AddWhenConfigured("Twitter", "Twitter", section => new LoginProviders.TwitterProviderConfiguration(section));
        AddWhenConfigured("WhatsApp", "WhatsApp", section => new LoginProviders.WhatsAppProviderConfiguration(section));
        AddWhenConfigured("TestMode", "testmode", section => new LoginTestProviders.TestModeConfiguration(section));

        ApplyEntraCredentials(configuration, providers);

        return providers;

        void AddWhenConfigured(
            string sectionName,
            string providerCode,
            Func<IConfigurationSection, ProviderConfiguration> create)
        {
            IConfigurationSection hostSection = configuration.GetSection(sectionName);

            if (!hostSection.GetChildren().Any() && hostSection.Value is null)
                return;

            ProviderConfiguration? projectProvider = projectProviders.FirstOrDefault(provider =>
                string.Equals(provider.Code, providerCode, StringComparison.OrdinalIgnoreCase));
            IConfigurationSection effectiveSection = projectProvider is null
                ? hostSection
                : BuildMergedProviderSection(
                    configuration,
                    sectionName,
                    projectProvider);
            ProviderConfiguration provider = create(effectiveSection);

            if (projectProvider is not null)
            {
                provider.HandleUpdateOnly = effectiveSection.GetValue(
                    "HandleUpdateOnly",
                    projectProvider.HandleUpdateOnly);
            }

            providers.Add(provider);
        }
    }

    /// <summary>
    /// Gives a Microsoft provider declared without credentials the ones CoconutSharp's Entra
    /// reference wrote. An AppHost adds <c>new MicrosoftProviderConfiguration()</c> and references
    /// the Entra registration from the login project; the client Id, tenant and the certificate (or
    /// the local run's secret) then arrive under <c>Entra:*</c> rather than being restated under
    /// <c>Microsoft:*</c>. Explicit <c>Microsoft:*</c> values still win.
    /// </summary>
    private static void ApplyEntraCredentials(IConfiguration configuration, List<ProviderConfiguration> providers)
    {
        LoginProviders.MicrosoftProviderConfiguration? microsoft = providers
            .OfType<LoginProviders.MicrosoftProviderConfiguration>()
            .FirstOrDefault();

        if (microsoft is not null && !string.IsNullOrWhiteSpace(microsoft.ClientId))
            return;

        IConfigurationSection entra = configuration.GetSection("Entra");

        if (string.IsNullOrWhiteSpace(entra["ClientId"]))
            return;

        if (microsoft is null)
        {
            microsoft = new LoginProviders.MicrosoftProviderConfiguration();
            providers.Add(microsoft);
        }
        microsoft.ClientId = entra["ClientId"]!;
        microsoft.TenantId ??= entra["TenantId"];
        microsoft.ClientSecret ??= entra["ClientSecret"];
        microsoft.CertificateName ??= entra["CertificateName"];

        if (microsoft.VaultEndpoint is null && Uri.TryCreate(entra["VaultEndpoint"], UriKind.Absolute, out Uri? vaultEndpoint))
            microsoft.VaultEndpoint = vaultEndpoint;
    }

    private static IConfigurationSection BuildMergedProviderSection(
        IConfiguration hostConfiguration,
        string sectionName,
        ProviderConfiguration projectProvider)
    {
        Dictionary<string, string?> defaults = new()
        {
            [$"{sectionName}:Label"] = projectProvider.Label,
            [$"{sectionName}:HandleUpdateOnly"] = projectProvider.HandleUpdateOnly.ToString()
        };

        switch (projectProvider)
        {
            case LoginProviders.MicrosoftProviderConfiguration microsoft:
                defaults[$"{sectionName}:ClientId"] = microsoft.ClientId;
                defaults[$"{sectionName}:ClientSecret"] = microsoft.ClientSecret;
                defaults[$"{sectionName}:TenantId"] = microsoft.TenantId;
                defaults[$"{sectionName}:VaultEndpoint"] = microsoft.VaultEndpoint?.AbsoluteUri;
                defaults[$"{sectionName}:CertificateName"] = microsoft.CertificateName;
                defaults[$"{sectionName}:Audience"] = microsoft.Audience.ToString();
                break;
            case LoginProviders.GoogleProviderConfiguration google:
                defaults[$"{sectionName}:ClientId"] = google.ClientId;
                defaults[$"{sectionName}:ClientSecret"] = google.ClientSecret;
                break;
            case LoginProviders.FacebookProviderConfiguration facebook:
                defaults[$"{sectionName}:ClientId"] = facebook.ClientId;
                defaults[$"{sectionName}:ClientSecret"] = facebook.ClientSecret;
                break;
            case LoginProviders.TwitterProviderConfiguration twitter:
                defaults[$"{sectionName}:ClientId"] = twitter.ClientId;
                defaults[$"{sectionName}:ClientSecret"] = twitter.ClientSecret;
                break;
            case LoginProviders.WhatsAppProviderConfiguration whatsApp:
                defaults[$"{sectionName}:RequestUri"] = whatsApp.RequestUri;
                defaults[$"{sectionName}:Authorization"] = whatsApp.Authorization;
                defaults[$"{sectionName}:Template"] = whatsApp.Template;
                defaults[$"{sectionName}:Language"] = whatsApp.Language;
                break;
            case LoginTestProviders.TestModeConfiguration testMode:
                defaults[$"{sectionName}:IsEnabled"] = testMode.IsEnabled.ToString();
                break;
        }

        IConfiguration merged = new ConfigurationBuilder()
            .AddInMemoryCollection(defaults)
            .AddConfiguration(hostConfiguration)
            .Build();

        return merged.GetSection(sectionName);
    }
}

