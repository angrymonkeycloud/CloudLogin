using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;

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
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The CloudLogin resource name.</param>
    /// <param name="configure">Optional shared CloudLogin configuration.</param>
    /// <returns>
    /// A native Aspire project-resource builder. Hold it in a <c>var</c>: the returned type is what
    /// carries this package's <c>WithReference</c> overloads for Cosmos and Azure Storage, and
    /// typing the variable as <see cref="IResourceBuilder{T}"/> hides them again.
    /// </returns>
    public static ICloudLoginServerBuilder AddCloudLogin<TProject>(
        this IDistributedApplicationBuilder builder,
        string name = "login",
        Action<CloudLoginWebConfiguration>? configure = null)
        where TProject : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        CloudLoginWebConfiguration configuration = new();
        configure?.Invoke(configuration);

        CloudLoginServerAnnotation annotation = new();
        annotation.Apply(configuration);

        IResourceBuilder<ProjectResource> project = builder.AddProject<TProject>(name)
            .WithExternalHttpEndpoints()
            .WithAnnotation(annotation)
            .WithAnnotation(new CoconutSharp.Aspire.Hosting.CoconutEntraSignInAnnotation("/signin-microsoft"));

        project.ApplyCloudLoginDefaults();

        // The V3 identity secret, generated once and kept. Wired unconditionally so a CloudLogin
        // under an AppHost simply works: the server requires the secret and will not invent one,
        // and an AppHost is the only place with somewhere durable to keep the same value across
        // restarts. WithIdentityHmacSecret afterwards replaces it.
        project.WithEnvironment(
            CloudLoginConfigurationKeys.IdentityHmacSecretVariable,
            IdentityHmacSecretParameter.AddFor(builder, name));

        if (configure is not null)
            CloudLoginConfigurationProjection.Apply(project, configuration);
        return new CloudLoginServerBuilder(project);
    }

    /// <summary>
    /// Adds the packaged CloudLogin server without requiring an application project.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The CloudLogin resource name.</param>
    /// <param name="configure">Optional shared CloudLogin configuration.</param>
    /// <returns>
    /// A CloudLogin project resource that can be referenced and deployed like any other project.
    /// Hold it in a <c>var</c> - see <see cref="AddCloudLogin{TProject}"/>.
    /// </returns>
    public static ICloudLoginServerBuilder AddCloudLogin(
        this IDistributedApplicationBuilder builder,
        string name = "login",
        Action<CloudLoginWebConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        CloudLoginWebConfiguration configuration = new();
        configure?.Invoke(configuration);

        CloudLoginServerAnnotation annotation = new();
        annotation.Apply(configuration);

        IResourceBuilder<ProjectResource> project = builder
            .AddProject(name, CloudLoginStandaloneProject.Extract())
            .WithHttpEndpoint(name: "http")
            .WithExternalHttpEndpoints()
            .WithAnnotation(annotation)
            .WithAnnotation(new CoconutSharp.Aspire.Hosting.CoconutEntraSignInAnnotation("/signin-microsoft"));

        project.ApplyCloudLoginDefaults();

        // The V3 identity secret, generated once and kept. Wired unconditionally so a CloudLogin
        // under an AppHost simply works: the server requires the secret and will not invent one,
        // and an AppHost is the only place with somewhere durable to keep the same value across
        // restarts. WithIdentityHmacSecret afterwards replaces it.
        project.WithEnvironment(
            CloudLoginConfigurationKeys.IdentityHmacSecretVariable,
            IdentityHmacSecretParameter.AddFor(builder, name));

        if (configure is not null)
            CloudLoginConfigurationProjection.Apply(project, configuration);
        return new CloudLoginServerBuilder(project);
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
    private readonly HashSet<string> _consumers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _serviceCallers = new(StringComparer.Ordinal);

    /// <summary>The core database name, mirroring <c>CloudLoginCoreConfiguration.DatabaseId</c>.</summary>
    internal string CoreDatabaseId { get; private set; } = CloudLoginCoreContainers.DefaultDatabaseId;

    internal void Apply(CloudLoginWebConfiguration configuration) =>
        CoreDatabaseId = configuration.Core.DatabaseId;

    internal bool AddConsumer(string name) => _consumers.Add(name);

    /// <summary>
    /// Grants <paramref name="name"/> the backend service channel, unless it already holds one.
    /// </summary>
    /// <returns><see langword="false"/> when this caller was already granted access - the call is
    /// then a no-op, which is what keeps <c>WithServiceAccess</c> safe to call more than once.</returns>
    internal bool AddServiceCaller(string name) => _serviceCallers.Add(name);

    /// <summary>
    /// The index this authority's <c>CloudLogin:ServiceKeys</c> list has grown to. Read immediately
    /// after a caller is added, so its own key lands at <c>Count - 1</c>.
    /// </summary>
    internal int ServiceCallerCount => _serviceCallers.Count;

}
