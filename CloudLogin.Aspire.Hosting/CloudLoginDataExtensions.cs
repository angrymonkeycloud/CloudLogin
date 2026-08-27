using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// Points the CloudLogin server at the Cosmos account holding its users and the Azure Storage
/// account holding its files, with the two verbs an Aspire AppHost already uses.
/// </summary>
/// <remarks>
/// <para>
/// These are ordinary <c>WithReference</c> overloads: each does exactly what a plain Aspire
/// reference does, and then additionally maps the referenced resource onto the configuration keys
/// the CloudLogin server itself binds (<see cref="CloudLoginConfigurationKeys"/>). Nothing is
/// resolved implicitly and nothing is created on the host's behalf - an account CloudLogin is never
/// pointed at is an account CloudLogin never uses.
/// </para>
/// <para>
/// Waiting is left to Aspire: pair each reference with <c>WaitFor</c>. During a local run the
/// storage value is assembled from the emulator's allocated endpoints, which exist only once the
/// emulator is running, so a reference without a wait leaves the key empty on first start.
/// </para>
/// </remarks>
public static class CloudLoginDataExtensions
{
    /// <summary>
    /// The Cosmos DB account holding CloudLogin's user store, and the database and container
    /// inside it.
    /// </summary>
    /// <remarks>
    /// The database and container are provisioned under the names <c>AddCloudLogin</c> was
    /// configured with, so two components can share one account without sharing data. Their names
    /// stay in step if CloudLogin's own configuration renames them afterwards.
    /// </remarks>
    public static TBuilder WithReference<TBuilder>(
        this TBuilder builder,
        IResourceBuilder<AzureCosmosDBResource> cosmos)
        where TBuilder : ICloudLoginServerBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cosmos);

        // Standard Aspire behaviour first - ConnectionStrings__{name}, exactly as a plain reference
        // sets it. Qualified rather than called as an extension so it reaches Aspire's method and
        // not this one.
        global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(builder, cosmos);

        CloudLoginServerAnnotation annotation =
            CloudLoginHostingExtensions.GetCloudLoginServer(builder, nameof(WithReference));

        IResourceBuilder<AzureCosmosDBDatabaseResource> database =
            cosmos.AddCosmosDatabase($"{builder.Resource.Name}-database", annotation.DatabaseId);

        IResourceBuilder<AzureCosmosDBContainerResource> container =
            database.AddContainer($"{builder.Resource.Name}-container", "/pk", annotation.ContainerId);

        annotation.AddCosmosResources(database.Resource, container.Resource);

        builder.WithEnvironment(context =>
        {
            // The same value object a native reference injects: it resolves through Aspire's own
            // connection-string resolution - emulator, environment redirects, readiness - rather
            // than reading raw endpoints. An environment that redirects this account's connection
            // string therefore reaches CloudLogin's key too, without this package knowing how.
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.ConnectionString] =
                new ConnectionStringReference(cosmos.Resource, optional: false);

            // Read now rather than captured above: CloudLogin's own configuration may rename either
            // after this call, and the provisioned resources above follow it.
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.DatabaseId] = annotation.DatabaseId;
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.ContainerId] = annotation.ContainerId;

            // The Linux-based Cosmos emulator speaks Gateway mode only, and nothing but a local run
            // uses an emulator.
            if (context.ExecutionContext.IsRunMode && cosmos.Resource.IsEmulator)
                context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.GatewayMode] = "true";
        });

        return builder;
    }

    /// <summary>
    /// The Azure Storage account holding CloudLogin's profile pictures and per-user security
    /// documents.
    /// </summary>
    /// <remarks>
    /// Takes the account rather than one of its blob/table/queue children, because CloudLogin
    /// reaches all of them through a single connection string and a child's own string only covers
    /// the service it belongs to - a blob-scoped string hands the table SDK an endpoint it cannot
    /// authenticate against, which surfaces as a 403 the first time a table is opened. An account
    /// exposes no connection string of its own, so unlike the Cosmos overload there is no stock
    /// Aspire reference to preserve here; this is purely additive.
    /// </remarks>
    public static TBuilder WithReference<TBuilder>(
        this TBuilder builder,
        IResourceBuilder<AzureStorageResource> storage)
        where TBuilder : ICloudLoginServerBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storage);

        CloudLoginHostingExtensions.GetCloudLoginServer(builder, nameof(WithReference));

        // A blob child purely so a deployed environment has something to resolve against, and so an
        // environment that redirects the account's connection string reaches this value as well.
        // Named after the consumer, so a second component referencing the same account adds its own
        // rather than colliding with this one.
        IResourceBuilder<AzureBlobStorageResource> blobs = storage.AddBlobs($"{builder.Resource.Name}-blobs");

        builder.WithEnvironment(context =>
        {
            if (context.ExecutionContext.IsRunMode)
            {
                if (storage.Resource.IsEmulator && CloudLoginAzurite.ConnectionString(storage.Resource) is { } emulator)
                    context.EnvironmentVariables[CloudLoginConfigurationKeys.Storage.ConnectionString] = emulator;

                return;
            }

            context.EnvironmentVariables[CloudLoginConfigurationKeys.Storage.ConnectionString] =
                new ConnectionStringReference(blobs.Resource, optional: true);

            // A provisioned account has no key, which is the point of managed identity: the account
            // name is what makes credential access possible.
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Storage.AccountName] =
                storage.Resource.NameOutputReference;
        });

        return builder;
    }

    /// <summary>
    /// CloudLogin's user store, reached by credential rather than by key: the account endpoint is
    /// configured and the server authenticates as whatever identity it runs under.
    /// </summary>
    /// <remarks>
    /// For an account this application does not model - nothing is provisioned, and the database
    /// and container are expected to exist. Use <c>WithReference</c> for an account the AppHost
    /// declares.
    /// </remarks>
    public static ICloudLoginServerBuilder WithCloudLoginCosmosEndpoint(
        this ICloudLoginServerBuilder builder,
        string accountEndpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountEndpoint);

        CloudLoginServerAnnotation annotation =
            CloudLoginHostingExtensions.GetCloudLoginServer(builder, nameof(WithCloudLoginCosmosEndpoint));

        builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.AccountEndpoint] = accountEndpoint;
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.DatabaseId] = annotation.DatabaseId;
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.ContainerId] = annotation.ContainerId;
        });

        return builder;
    }

    /// <summary>
    /// CloudLogin's file store, reached by credential rather than by key. See
    /// <see cref="WithCloudLoginCosmosEndpoint"/>.
    /// </summary>
    public static ICloudLoginServerBuilder WithCloudLoginStorageAccount(
        this ICloudLoginServerBuilder builder,
        string accountName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        CloudLoginHostingExtensions.GetCloudLoginServer(builder, nameof(WithCloudLoginStorageAccount));

        builder.WithEnvironment(CloudLoginConfigurationKeys.Storage.AccountName, accountName);
        return builder;
    }
}

/// <summary>The Azurite emulator's connection string, assembled from its allocated endpoints.</summary>
internal static class CloudLoginAzurite
{
    // Azurite's well-known development credentials. Public constants, not secrets.
    private const string AccountName = "devstoreaccount1";
    private const string AccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    /// <summary>
    /// The emulator's connection string, or <see langword="null"/> while its endpoints have not
    /// been allocated yet.
    /// </summary>
    /// <remarks>
    /// Environment callbacks also run for early dependency analysis, before endpoints exist; only
    /// the start-time invocation produces a value, which is what <c>WaitFor</c> guarantees. The
    /// endpoints are addressed by IP because the Azure Storage SDK drops the account path from a
    /// <c>localhost</c> endpoint - path-style parsing applies to IP hosts only - which is what
    /// makes Azurite reject the request.
    /// </remarks>
    public static string? ConnectionString(AzureStorageResource storage)
    {
        EndpointReference blob = new(storage, "blob");
        EndpointReference queue = new(storage, "queue");
        EndpointReference table = new(storage, "table");

        if (!blob.IsAllocated || !queue.IsAllocated || !table.IsAllocated)
            return null;

        return $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};" +
            $"BlobEndpoint={Url(blob)}/{AccountName};" +
            $"QueueEndpoint={Url(queue)}/{AccountName};" +
            $"TableEndpoint={Url(table)}/{AccountName};";

        static string Url(EndpointReference endpoint) =>
            endpoint.Url.Replace("://localhost", "://127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }
}
