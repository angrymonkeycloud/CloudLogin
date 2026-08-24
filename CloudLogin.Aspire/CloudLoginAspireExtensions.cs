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

        configuration.Cosmos = BuildCosmos(builder.Configuration, credential);
        configuration.AzureStorage = BuildStorage(builder.Configuration, credential);

        return configuration;
    }

    /// <summary>
    /// Builds the Cosmos configuration from the <c>Cosmos</c> section, attaching a credential when
    /// the section names an endpoint rather than carrying a connection string.
    /// </summary>
    public static CosmosConfiguration BuildCosmos(IConfiguration configuration, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        CosmosConfiguration cosmos = new(configuration.GetSection("Cosmos"));

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
            cosmos.Credential = credential ?? new DefaultAzureCredential();

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

        AzureStorageConfiguration storage = new(configuration.GetSection("Storage"));

        // Same as Cosmos: a storage account provisioned for credential access publishes its blob
        // endpoint where a connection string would otherwise go. The account name is the first label
        // of that host.
        if (string.IsNullOrWhiteSpace(storage.AccountName) && IsBareEndpoint(storage.ConnectionString))
        {
            storage = new AzureStorageConfiguration
            {
                AccountName = new Uri(storage.ConnectionString!).Host.Split('.')[0],
                ContainerName = storage.ContainerName
            };
        }

        if (!string.IsNullOrWhiteSpace(storage.AccountName) && string.IsNullOrWhiteSpace(storage.ConnectionString))
            storage.Credential = credential ?? new DefaultAzureCredential();

        return storage;
    }
}
