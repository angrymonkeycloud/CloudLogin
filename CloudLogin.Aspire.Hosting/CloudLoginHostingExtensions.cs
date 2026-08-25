using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// Adds a CloudLogin server to a distributed application and wires the resources it depends on.
/// </summary>
/// <remarks>
/// <para>
/// The Aspire hosting half of CloudLogin's integration: it configures an ordinary Aspire project
/// resource, so every Aspire API keeps working on the result and nothing here replaces CloudLogin's
/// own configuration model. A host that would rather bind configuration itself can ignore this
/// package entirely - the keys it writes are the same ones documented for appsettings.json, and are
/// published as <see cref="CloudLoginConfigurationKeys"/> rather than spelled out at call sites.
/// </para>
/// <para>
/// Deployment strategy is deliberately not this package's concern. It wires a project to the
/// resources it was handed, in whatever form Aspire resolves them; a deployment tool layered on top
/// decides whether a given environment reaches those resources by key, by credential, or by an
/// externally supplied connection string.
/// </para>
/// </remarks>
public static class CloudLoginHostingExtensions
{
    /// <summary>
    /// Adds the application's CloudLogin server project, marked so the wiring helpers below can
    /// tell it apart from every other project in the model.
    /// </summary>
    public static CloudLoginProjectBuilder AddCloudLogin<TProject>(
        this IDistributedApplicationBuilder builder,
        string name = "login")
        where TProject : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        IResourceBuilder<ProjectResource> project = builder.AddProject<TProject>(name)
            .WithExternalHttpEndpoints()
            .WithAnnotation(new CloudLoginServerAnnotation());

        return new CloudLoginProjectBuilder(project).ApplyCloudLoginDefaults();
    }

    /// <summary>
    /// Adds the packaged CloudLogin server without requiring an application project.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The CloudLogin resource name.</param>
    /// <returns>A CloudLogin project resource that can be referenced and deployed like any other project.</returns>
    public static CloudLoginProjectBuilder AddCloudLogin(
        this IDistributedApplicationBuilder builder,
        string name = "login")
    {
        ArgumentNullException.ThrowIfNull(builder);

        IResourceBuilder<ProjectResource> project = builder
            .AddProject(name, CloudLoginStandaloneProject.Extract())
            .WithHttpEndpoint(name: "http")
            .WithExternalHttpEndpoints()
            .WithAnnotation(new CloudLoginServerAnnotation());


        return new CloudLoginProjectBuilder(project).ApplyCloudLoginDefaults();
    }

    /// <summary>
    /// References an already-deployed CloudLogin server by URL. Nothing is deployed for it - this
    /// is how an application points at an authority somebody else runs.
    /// </summary>
    public static IResourceBuilder<ExternalServiceResource> AddCloudLogin(
        this IDistributedApplicationBuilder builder,
        string name,
        string url)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return builder.AddExternalService(name, url);
    }

    /// <summary>
    /// Wires CloudLogin's user store to a Cosmos DB resource: locally the emulator, in a deployment
    /// whatever that resource resolves to.
    /// </summary>
    /// <param name="builder">The CloudLogin server project.</param>
    /// <param name="cosmos">The Cosmos DB resource holding the user store.</param>
    /// <param name="databaseId">Database holding the user container.</param>
    /// <param name="containerId">Container holding user records.</param>
    public static IResourceBuilder<ProjectResource> WithCloudLoginCosmos(
        this IResourceBuilder<ProjectResource> builder,
        IResourceBuilder<AzureCosmosDBResource> cosmos,
        string databaseId = "Users",
        string containerId = "Data")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cosmos);
        EnsureCloudLoginServer(builder, nameof(WithCloudLoginCosmos));

        CloudLoginServerAnnotation annotation = GetCloudLoginServer(builder, nameof(WithCloudLoginCosmos));
        annotation.Apply(CloudLoginConfigurationKeys.Cosmos.DatabaseId, databaseId);
        annotation.Apply(CloudLoginConfigurationKeys.Cosmos.ContainerId, containerId);

        IResourceBuilder<AzureCosmosDBDatabaseResource> database = cosmos.AddCosmosDatabase(
            $"{builder.Resource.Name}-cloudlogin-database", annotation.DatabaseId);
        IResourceBuilder<AzureCosmosDBContainerResource> container = database.AddContainer(
            $"{builder.Resource.Name}-cloudlogin-container", "/pk", annotation.ContainerId);
        annotation.AddCosmosResources(database.Resource, container.Resource);

        // Deliberately no WithReference: that would add Aspire's own ConnectionStrings__cosmos alias
        // as well, and CloudLogin reads Cosmos:ConnectionString. On a deployed site the alias is a
        // second copy of the same credential that nothing reads.
        builder
            .WithEnvironment(CloudLoginConfigurationKeys.Cosmos.DatabaseId, databaseId)
            .WithEnvironment(CloudLoginConfigurationKeys.Cosmos.ContainerId, containerId);

        // In a local run the connection string is built from the emulator container's allocated
        // endpoints, so starting the server first hands it nothing to connect to - and CloudLogin
        // opens its user container during startup, which turns that into a crash rather than a retry.
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
            builder.WaitFor(cosmos);

        return builder.WithEnvironment(context =>
        {
            // ConnectionStringReference is the value object a native WithReference injects: it
            // resolves through the resource's own connection-string logic - emulator, redirects,
            // readiness - rather than assembling one from endpoints this package cannot see.
            context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.ConnectionString] =
                new ConnectionStringReference(cosmos.Resource, optional: true);

            // The Linux-based Cosmos emulator speaks Gateway mode only, and nothing but a local run
            // uses an emulator.
            if (context.ExecutionContext.IsRunMode && cosmos.Resource.IsEmulator)
                context.EnvironmentVariables[CloudLoginConfigurationKeys.Cosmos.GatewayMode] = "true";
        });
    }

    /// <summary>
    /// Wires CloudLogin's blob storage - profile pictures and per-user security documents - to an
    /// Azure Storage account.
    /// </summary>
    /// <remarks>
    /// Takes the account rather than one of its children so a local run gets the emulator's own
    /// connection string, assembled from the endpoints Azurite is actually listening on. In a
    /// deployment the account exposes no single connection string, so the value resolves through a
    /// blob child - which an environment can redirect to an externally supplied connection string,
    /// or replace outright with <see cref="WithCloudLoginStorageAccount"/> for credential access.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithCloudLoginStorage(
        this IResourceBuilder<ProjectResource> builder,
        IResourceBuilder<AzureStorageResource> storage,
        string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storage);
        EnsureCloudLoginServer(builder, nameof(WithCloudLoginStorage));

        // See WithCloudLoginCosmos: locally this resolves from a running Azurite container.
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
            builder.WaitFor(storage);

        IResourceBuilder<AzureBlobStorageResource> blobs = storage.AddBlobs($"{storage.Resource.Name}-cloudlogin");

        builder.WithEnvironment(context =>
        {
            if (context.ExecutionContext.IsRunMode)
            {
                if (storage.Resource.IsEmulator && AzuriteConnectionString(storage.Resource) is { } emulator)
                    context.EnvironmentVariables[CloudLoginConfigurationKeys.Storage.ConnectionString] = emulator;

                return;
            }

            context.EnvironmentVariables[CloudLoginConfigurationKeys.Storage.ConnectionString] =
                new ConnectionStringReference(blobs.Resource, optional: true);
        });

        if (containerName is not null)
            builder.WithEnvironment(CloudLoginConfigurationKeys.Storage.ContainerName, containerName);

        return builder;
    }

    // Azurite's well-known development credentials. Public constants, not secrets.
    private const string AzuriteAccountName = "devstoreaccount1";
    private const string AzuriteAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    /// <summary>
    /// Assembles the emulator's connection string from its allocated endpoints, or
    /// <see langword="null"/> while they have not been allocated yet.
    /// </summary>
    /// <remarks>
    /// Environment callbacks also run for early dependency analysis, before endpoints exist; only the
    /// start-time invocation produces a value. The endpoints are addressed by IP because the Azure
    /// Storage SDK drops the account path from a <c>localhost</c> endpoint - path-style parsing
    /// applies to IP hosts only - which is what makes Azurite reject the request.
    /// </remarks>
    private static string? AzuriteConnectionString(AzureStorageResource storage)
    {
        EndpointReference blob = new(storage, "blob");
        EndpointReference queue = new(storage, "queue");
        EndpointReference table = new(storage, "table");

        if (!blob.IsAllocated || !queue.IsAllocated || !table.IsAllocated)
            return null;

        return $"DefaultEndpointsProtocol=http;AccountName={AzuriteAccountName};AccountKey={AzuriteAccountKey};" +
            $"BlobEndpoint={Url(blob)}/{AzuriteAccountName};" +
            $"QueueEndpoint={Url(queue)}/{AzuriteAccountName};" +
            $"TableEndpoint={Url(table)}/{AzuriteAccountName};";

        static string Url(EndpointReference endpoint) =>
            endpoint.Url.Replace("://localhost", "://127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reaches CloudLogin by credential rather than by key: the account name is configured, and the
    /// server authenticates as whatever identity it is running under.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="WithCloudLoginStorage(IResourceBuilder{ProjectResource}, IResourceBuilder{AzureStorageResource}, string?)"/>:
    /// an account name is only usable when the application actually has an identity with data-plane
    /// access to that account, which is a property of the deployment rather than of the wiring.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithCloudLoginStorageAccount(
        this IResourceBuilder<ProjectResource> builder,
        string accountName,
        string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        EnsureCloudLoginServer(builder, nameof(WithCloudLoginStorageAccount));

        builder.WithEnvironment(CloudLoginConfigurationKeys.Storage.AccountName, accountName);

        if (containerName is not null)
            builder.WithEnvironment(CloudLoginConfigurationKeys.Storage.ContainerName, containerName);

        return builder;
    }

    /// <summary>
    /// Reaches CloudLogin's user store by credential rather than by key.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithCloudLoginCosmosEndpoint(
        this IResourceBuilder<ProjectResource> builder,
        string accountEndpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountEndpoint);
        EnsureCloudLoginServer(builder, nameof(WithCloudLoginCosmosEndpoint));

        return builder.WithEnvironment(CloudLoginConfigurationKeys.Cosmos.AccountEndpoint, accountEndpoint);
    }

    /// <summary>
    /// Points a project at the application's CloudLogin server: an ordinary Aspire reference, plus
    /// the authority URL the CloudLogin components read.
    /// </summary>
    public static IResourceBuilder<T> WithCloudLogin<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        string? endpointName = null,
        string configurationKey = CloudLoginConfigurationKeys.LoginUrl)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        // The reference is a local-run concern: it brings Aspire's service-discovery variables, which
        // a deployed site reaches nothing through - it has a real URL. Adding them in a deployment
        // would put a handful of unresolvable endpoint references on the site for nothing.
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
            builder.WithReference(cloudLogin);

        return builder.WithEnvironment(configurationKey, GetPreferredEndpoint(cloudLogin, endpointName));
    }

    /// <summary>Points a project at an already-deployed CloudLogin server.</summary>
    public static IResourceBuilder<T> WithCloudLogin<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<ExternalServiceResource> cloudLogin,
        string configurationKey = CloudLoginConfigurationKeys.LoginUrl)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        return builder.WithEnvironment(configurationKey, cloudLogin);
    }

    /// <summary>
    /// Tells CloudLogin which origins it may return a signed-in user to. Without this the allow-list
    /// is whatever the server itself was configured with, which cannot name ports a local run
    /// assigns fresh on every start.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithCloudLoginRedirectOrigins<T>(
        this IResourceBuilder<ProjectResource> builder,
        params IResourceBuilder<T>[] origins)
        where T : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(origins);
        EnsureCloudLoginServer(builder, nameof(WithCloudLoginRedirectOrigins));

        for (int index = 0; index < origins.Length; index++)
        {
            builder.WithEnvironment(
                $"{CloudLoginConfigurationKeys.AllowedRedirectOrigins}:{index}",
                origins[index].GetEndpoint("https"));
        }

        return builder;
    }

    /// <summary>
    /// Tells CloudLogin which origins it may return a signed-in user to, by URL.
    /// </summary>
    /// <remarks>
    /// The overload a deployed environment needs. Endpoint references resolve to nothing outside a
    /// running application, so an environment whose sites have real, known addresses names them
    /// instead - and must, because an allow-list that does not contain the site a user started from
    /// refuses to send them back to it, turning a successful sign-in into a failed one.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithCloudLoginRedirectOrigins(
        this IResourceBuilder<ProjectResource> builder,
        params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(origins);
        EnsureCloudLoginServer(builder, nameof(WithCloudLoginRedirectOrigins));

        for (int index = 0; index < origins.Length; index++)
            builder.WithEnvironment($"{CloudLoginConfigurationKeys.AllowedRedirectOrigins}:{index}", origins[index]);

        return builder;
    }

    /// <summary>
    /// The project's preferred web endpoint: the named one when given, otherwise <c>https</c> when
    /// the project actually exposes it, else <c>http</c>. Referencing an endpoint the launch profile
    /// does not define would silently stop the project starting during a local run.
    /// </summary>
    internal static EndpointReference GetPreferredEndpoint(IResourceBuilder<ProjectResource> project, string? endpointName)
    {
        if (endpointName is not null)
            return project.GetEndpoint(endpointName);

        List<string> endpointNames = [.. project.Resource.Annotations.OfType<EndpointAnnotation>().Select(endpoint => endpoint.Name)];

        if (endpointNames.Count == 0 || endpointNames.Contains("https", StringComparer.OrdinalIgnoreCase))
            return project.GetEndpoint("https");

        if (endpointNames.Contains("http", StringComparer.OrdinalIgnoreCase))
            return project.GetEndpoint("http");

        return project.GetEndpoint(endpointNames[0]);
    }

    private static void EnsureCloudLoginServer(IResourceBuilder<ProjectResource> builder, string method)
    {
        _ = GetCloudLoginServer(builder, method);
    }

    internal static CloudLoginServerAnnotation GetCloudLoginServer(
        IResourceBuilder<ProjectResource> builder,
        string method) =>
        builder.Resource.Annotations.OfType<CloudLoginServerAnnotation>().LastOrDefault()
        ?? throw new DistributedApplicationException(
            $"Project '{builder.Resource.Name}' was not added with AddCloudLogin<TProject>(), so {method} has " +
            "nothing to configure. These helpers write the CloudLogin server's own configuration section and " +
            "only apply to the project hosting it.");
}

/// <summary>Marks the project hosting the application's CloudLogin server.</summary>
public sealed class CloudLoginServerAnnotation : IResourceAnnotation
{
    private readonly List<(AzureCosmosDBDatabaseResource Database, AzureCosmosDBContainerResource Container)> _cosmosResources = [];

    internal string DatabaseId { get; private set; } = "Users";
    internal string ContainerId { get; private set; } = "Data";

    internal void Apply(string key, string value)
    {
        switch (key)
        {
            case CloudLoginConfigurationKeys.Cosmos.DatabaseId:
                DatabaseId = value;
                break;
            case CloudLoginConfigurationKeys.Cosmos.ContainerId:
                ContainerId = value;
                break;
            default:
                return;
        }

        foreach ((AzureCosmosDBDatabaseResource database, AzureCosmosDBContainerResource container) in _cosmosResources)
        {
            database.DatabaseName = DatabaseId;
            container.ContainerName = ContainerId;
        }
    }

    internal void AddCosmosResources(
        AzureCosmosDBDatabaseResource database,
        AzureCosmosDBContainerResource container) =>
        _cosmosResources.Add((database, container));
}
