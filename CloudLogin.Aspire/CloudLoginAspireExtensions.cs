using AngryMonkey.CloudLogin.Server;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AngryMonkey.CloudLogin.Aspire;

/// <summary>
/// Binds a <see cref="CloudLoginWebConfiguration"/> from configuration the way an Aspire host
/// supplies it, so a CloudLogin server hosted under Aspire configures itself from what the AppHost
/// wired rather than from values repeated in its own appsettings.json.
/// </summary>
/// <remarks>
/// <para>
/// This is a convenience over the ordinary configuration model, not a replacement for it. The same
/// keys work when no AppHost is involved - an appsettings.json, a user secret, an environment
/// variable set by hand - which is what keeps a CloudLogin server runnable with no Aspire anywhere
/// in the picture.
/// </para>
/// <para>
/// The credential is the part a host cannot express in configuration: an account name or endpoint
/// is a value, but the thing that authenticates against it is an object. So when the configuration
/// names an account without a key, this attaches a credential - by default
/// <see cref="DefaultAzureCredential"/>, which picks up the identity the application is running as.
/// </para>
/// </remarks>
public static class CloudLoginAspireExtensions
{
    /// <summary>
    /// Fills in the Cosmos and Azure Storage sections of <paramref name="configuration"/> from the
    /// host's configuration, attaching <paramref name="credential"/> wherever an account is named
    /// without a key.
    /// </summary>
    /// <param name="configuration">The CloudLogin configuration being built.</param>
    /// <param name="builder">The host whose configuration supplies the values.</param>
    /// <param name="credential">
    /// The credential to authenticate with when an account is named without a key. Defaults to
    /// <see cref="DefaultAzureCredential"/>, which resolves the identity the application runs as -
    /// including a user-assigned identity selected by <c>AZURE_CLIENT_ID</c>.
    /// </param>
    /// <returns>The same configuration, so this composes into an object initializer chain.</returns>
    public static CloudLoginWebConfiguration BindAspireResources(
        this CloudLoginWebConfiguration configuration,
        IHostApplicationBuilder builder,
        TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(builder);

        configuration.Cosmos = ApplyCosmosConfiguration(builder.Configuration, configuration.Cosmos, credential);
        configuration.AzureStorage = ApplyStorageConfiguration(builder.Configuration, configuration.AzureStorage, credential);

        return configuration;
    }

    /// <summary>
    /// Builds the Cosmos configuration from the <c>Cosmos</c> section, attaching a credential when
    /// the section names an endpoint rather than carrying a connection string.
    /// </summary>
    public static CosmosConfiguration BuildCosmos(IConfiguration configuration, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return ApplyCosmosConfiguration(configuration, new CosmosConfiguration(configuration.GetSection("Cosmos")), credential);
    }

    private static CosmosConfiguration ApplyCosmosConfiguration(IConfiguration configuration, CosmosConfiguration cosmos, TokenCredential? credential)
    {
        IConfigurationSection section = configuration.GetSection("Cosmos");

        if (section.Exists())
        {
            section.Bind(cosmos);

            if (section["IncludeLegacySchema"] is null && section["UseLegacySchema"] is not null)
                cosmos.IncludeLegacySchema = section.GetValue<bool>("UseLegacySchema");

            if (section["SaveIdMode"] is null && Enum.TryParse(section["IdFormat"], ignoreCase: true, out IdSaveMode saveMode))
                cosmos.SaveIdMode = saveMode;

            bool hasEndpoint = section["AccountEndpoint"] is not null;
            bool hasConnectionString = section["ConnectionString"] is not null;

            if (hasEndpoint)
            {
                cosmos.ConnectionString = null;
                cosmos.Credential = null;
            }
            else if (hasConnectionString && !IsBareEndpoint(cosmos.ConnectionString))
            {
                cosmos.AccountEndpoint = null;
                cosmos.Credential = null;
            }
        }

        // A Cosmos account Aspire provisioned for credential access has no key to put in a connection
        // string, so what it publishes as one is the account endpoint on its own. Recognising that
        // here is what lets the same configuration key carry either - a real connection string from
        // an environment that uses keys, or an endpoint from one that does not.
        if (string.IsNullOrWhiteSpace(cosmos.AccountEndpoint) && IsBareEndpoint(cosmos.ConnectionString))
        {
            cosmos.AccountEndpoint = cosmos.ConnectionString;
            cosmos.ConnectionString = null;
        }

        if (!string.IsNullOrWhiteSpace(cosmos.AccountEndpoint) && string.IsNullOrWhiteSpace(cosmos.ConnectionString))
            cosmos.Credential = credential ?? cosmos.Credential ?? new DefaultAzureCredential();

        return cosmos;
    }

    /// <summary>
    /// Whether a configured value is a bare service endpoint rather than a connection string. A
    /// connection string is a list of <c>key=value</c> pairs; an endpoint is just a URL.
    /// </summary>
    private static bool IsBareEndpoint(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains('=', StringComparison.Ordinal)
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "https" or "http";

    /// <summary>
    /// Builds the Azure Storage configuration from the <c>Storage</c> section, attaching a
    /// credential when the section names an account rather than carrying a connection string.
    /// </summary>
    public static AzureStorageConfiguration BuildStorage(IConfiguration configuration, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return ApplyStorageConfiguration(configuration, null, credential)
            ?? new AzureStorageConfiguration();
    }

    private static AzureStorageConfiguration? ApplyStorageConfiguration(
        IConfiguration configuration,
        AzureStorageConfiguration? projectStorage,
        TokenCredential? credential)
    {
        IConfigurationSection section = configuration.GetSection("Storage");

        if (!section.Exists())
        {
            if (projectStorage?.BlobEndpoint is not null && string.IsNullOrWhiteSpace(projectStorage.ConnectionString))
                projectStorage.Credential = credential ?? projectStorage.Credential ?? new DefaultAzureCredential();

            return projectStorage;
        }

        bool hasConnectionString = section["ConnectionString"] is not null;
        bool hasBlobEndpoint = section["BlobEndpoint"] is not null;
        bool hasAccountName = section["AccountName"] is not null;
        string? configuredConnectionString = section["ConnectionString"];

        if (hasConnectionString && IsBareEndpoint(configuredConnectionString))
        {
            hasBlobEndpoint = true;
            configuredConnectionString = null;
        }

        Uri? configuredBlobEndpoint = null;
        string? blobEndpointValue = section["BlobEndpoint"]
            ?? (hasBlobEndpoint ? section["ConnectionString"] : null);
        if (Uri.TryCreate(blobEndpointValue, UriKind.Absolute, out Uri? endpoint))
            configuredBlobEndpoint = endpoint;

        bool hostChangedAuthentication = hasConnectionString || hasBlobEndpoint || hasAccountName;
        bool usesHostCredential = hasBlobEndpoint || hasAccountName;
        string? accountName = hasAccountName
            ? section["AccountName"]
            : hasBlobEndpoint
                ? configuredBlobEndpoint?.Host.Split('.')[0]
                : hasConnectionString
                    ? null
                    : projectStorage?.AccountName;
        Uri? blobEndpoint = hasBlobEndpoint
            ? configuredBlobEndpoint
            : hasAccountName || hasConnectionString ? null : projectStorage?.BlobEndpoint;
        string? connectionString = usesHostCredential ? null : hasConnectionString ? configuredConnectionString : projectStorage?.ConnectionString;

        AzureStorageConfiguration storage = new()
        {
            AccountName = accountName,
            BlobEndpoint = blobEndpoint,
            ConnectionString = connectionString,
            ContainerName = section["ContainerName"] is { Length: > 0 } containerName
                ? containerName
                : projectStorage?.ContainerName ?? "users",
            PublicBaseUrl = section["PublicBaseUrl"]
                ?? (hostChangedAuthentication ? null : projectStorage?.PublicBaseUrl)
        };

        // Compatibility for deployments produced before managed identity values were projected
        // under Storage:BlobEndpoint.
        if (storage.BlobEndpoint is not null && string.IsNullOrWhiteSpace(storage.ConnectionString))
            storage.Credential = credential ?? projectStorage?.Credential ?? new DefaultAzureCredential();

        return storage;
    }
}
