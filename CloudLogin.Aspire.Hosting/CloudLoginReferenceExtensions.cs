using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// Connects application resources to CloudLogin and configures both sides of the trust
/// relationship from one resource reference.
/// </summary>
public static class CloudLoginReferenceExtensions
{
    /// <summary>
    /// Adds CloudLogin as a first-class reference, including local service discovery and all
    /// deployment-safe token and redirect configuration.
    /// </summary>
    /// <typeparam name="T">The relying resource type.</typeparam>
    /// <param name="builder">The relying application resource.</param>
    /// <param name="cloudLogin">The CloudLogin authority resource.</param>
    /// <returns>The relying resource builder.</returns>
    /// <remarks>
    /// Sign-in only. A server that also reads CloudLogin-owned records directly (CDM's external
    /// data provider, for example) additionally calls <see cref="WithServiceAccess{TBuilder}"/> -
    /// that channel bypasses user identity, so it is its own call rather than a flag here, granted
    /// only to the servers that need it.
    /// </remarks>
    public static TBuilder WithReference<TBuilder>(
        this TBuilder builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        Action<CloudLoginClientOptions>? configure = null)
        where TBuilder : IResourceBuilder<IResourceWithEnvironment>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        IResourceBuilder<IResourceWithEnvironment> consumer =
            builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource);

        if (!cloudLogin.Resource.Annotations.OfType<CloudLoginServerAnnotation>().Any())
        {
            global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(consumer, cloudLogin);
            return builder;
        }

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(consumer, cloudLogin);
            WaitForAuthority(consumer, cloudLogin);
        }

        ConfigureConsumer(consumer, cloudLogin, null, CloudLoginConfigurationKeys.LoginUrl, ClientOptions(configure));
        return builder;
    }

    /// <summary>
    /// References a CloudLogin that is not part of this AppHost. The application ends up with the same
    /// runtime configuration as when the authority is in the AppHost, so it signs in through exactly
    /// the same client library code.
    /// </summary>
    /// <param name="builder">The relying application resource.</param>
    /// <param name="cloudLogin">The existing authority, from <c>AddCloudLogin(name, url)</c>.</param>
    /// <param name="clientId">The name the application gives itself at the authority. Defaults to the resource name.</param>
    /// <param name="clientSecret">
    /// A secret key created in that authority's admin console (Secret keys). Defaults to one secret parameter named
    /// <c>{authority}-secret-key</c>, shared by every application referencing that authority, which Aspire asks for at run time and
    /// resolves from its secret store when deployed. It is never written to source.
    /// </param>
    /// <param name="audience">The audience of its tokens. Defaults to the client id.</param>
    /// <remarks>
    /// Nothing is registered at the authority: an application appears in its admin console the first time it signs someone in, and
    /// can be blocked there.
    /// </remarks>
    public static TBuilder WithReference<TBuilder>(
        this TBuilder builder,
        ICloudLoginExternalBuilder cloudLogin,
        string? clientId = null,
        IResourceBuilder<ParameterResource>? clientSecret = null,
        string? audience = null,
        Action<CloudLoginClientOptions>? configure = null)
        where TBuilder : IResourceBuilder<IResourceWithEnvironment>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        IResourceBuilder<IResourceWithEnvironment> consumer =
            builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource);

        CloudLoginClientOptions options = ClientOptions(configure);
        string id = string.IsNullOrWhiteSpace(clientId) ? builder.Resource.Name : clientId;

        consumer
            .WithEnvironment(CloudLoginConfigurationKeys.LoginUrl, cloudLogin)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.Authority, cloudLogin)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.Audience, string.IsNullOrWhiteSpace(audience) ? id : audience)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.ClientId, id);

        PublishOwnAddress(consumer, options);

        // Every website referencing the same external authority uses the same key unless one is passed: one secret parameter, asked
        // for once locally and resolved from the secret store when deployed.
        if (!options.IsPublic)
            consumer.WithEnvironment(CloudLoginConfigurationKeys.Client.ClientSecret, clientSecret ?? SharedParameter(builder.ApplicationBuilder, $"{Sanitize(cloudLogin.Resource.Name)}-secret-key", generate: false));

        return builder;
    }

    /// <summary>
    /// Connects a relying resource to CloudLogin while allowing an explicit authority endpoint
    /// and compatibility configuration key.
    /// </summary>
    /// <typeparam name="T">The relying resource type.</typeparam>
    /// <param name="builder">The relying application resource.</param>
    /// <param name="cloudLogin">The CloudLogin authority resource.</param>
    /// <param name="endpointName">The authority endpoint name, or the preferred endpoint when omitted.</param>
    /// <param name="configurationKey">
    /// The additional configuration key that receives the authority URI.
    /// </param>
    /// <returns>The relying resource builder.</returns>
    public static TBuilder WithCloudLogin<TBuilder>(
        this TBuilder builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        string? endpointName = null,
        string configurationKey = CloudLoginConfigurationKeys.LoginUrl)
        where TBuilder : IResourceBuilder<IResourceWithEnvironment>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        IResourceBuilder<IResourceWithEnvironment> consumer =
            builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(consumer, cloudLogin);
            WaitForAuthority(consumer, cloudLogin);
        }

        ConfigureConsumer(consumer, cloudLogin, endpointName, configurationKey, new CloudLoginClientOptions());
        return builder;
    }

    /// <summary>
    /// Opens the backend service channel between this resource and the application's own CloudLogin
    /// server: a shared secret, generated and configured on both ends, that reads CloudLogin-owned
    /// records (Business, Contact, Subscription, …) without a signed-in user.
    /// </summary>
    /// <param name="builder">The resource granted service access.</param>
    /// <param name="cloudLogin">The application's own CloudLogin server (added with <c>AddCloudLogin</c>).</param>
    /// <param name="endpointName">The authority endpoint name, or the preferred endpoint when omitted.</param>
    /// <returns>The caller's resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Independent of <see cref="WithReference{TBuilder}(TBuilder, IResourceBuilder{ProjectResource})"/>
    /// on purpose, rather than a flag on it: that call is a user signing in, and this is a backend
    /// credential that bypasses user identity entirely, so it is granted only to the servers that
    /// read CloudLogin's data directly - never implied by referencing the authority for sign-in.
    /// Call both when one resource needs each relationship; neither requires the other to have been
    /// called first.
    /// </para>
    /// <para>
    /// A key per caller, not one per authority: the authority accepts a list, so a caller can be
    /// revoked without invalidating the key every other caller already holds. Persisted, so a
    /// restart does not invalidate the key the other side already has, and long enough that
    /// CloudLogin's own validator accepts it (it rejects anything under 32 characters as guessable).
    /// </para>
    /// <para>
    /// The address matters as much as the secret: a local run gives the authority a port Aspire
    /// allocates fresh each time, so any base URL written into a settings file or a user secret is
    /// stale the moment it is written.
    /// </para>
    /// </remarks>
    public static TBuilder WithServiceAccess<TBuilder>(
        this TBuilder builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        string? endpointName = null)
        where TBuilder : IResourceBuilder<IResourceWithEnvironment>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        CloudLoginServerAnnotation annotation =
            CloudLoginHostingExtensions.GetCloudLoginServer(cloudLogin, nameof(WithServiceAccess));

        if (!annotation.AddServiceCaller(builder.Resource.Name))
            return builder;

        IResourceBuilder<IResourceWithEnvironment> consumer =
            builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource);

        EndpointReference authority = CloudLoginHostingExtensions.GetPreferredEndpoint(cloudLogin, endpointName);

        // Not persisted. This key authenticates a live channel whose two ends are both configured
        // from this one parameter - the caller presents it, the authority accepts it - so a restart
        // re-keys both at once and nothing written under the old value stops resolving. Persisting
        // would buy nothing and leave a working credential sitting in a developer's user secrets.
        // Deployed environments are unaffected: their value is resolved once per environment and
        // kept in deployment state, so redeploys reuse it.
        IResourceBuilder<ParameterResource> serviceKey = builder.ApplicationBuilder.AddParameter(
            Sanitize($"{cloudLogin.Resource.Name}-{builder.Resource.Name}-service-key"),
            new GenerateParameterDefault
            {
                MinLength = 48,
                Lower = true,
                Upper = true,
                Numeric = true,
                Special = false,
                MinLower = 8,
                MinUpper = 8,
                MinNumeric = 8
            },
            secret: true,
            persist: false);

        consumer
            .WithEnvironment(CloudLoginConfigurationKeys.Service.BaseUrl, authority)
            .WithEnvironment(CloudLoginConfigurationKeys.Service.CallerKey, serviceKey);

        // Indexed, because the authority binds a list: CloudLogin:ServiceKeys:0, :1, … A singular
        // key would bind to nothing and leave every service call rejected for want of one.
        cloudLogin.WithEnvironment(
            $"{CloudLoginConfigurationKeys.Service.AuthorityKeys}:{annotation.ServiceCallerCount - 1}",
            serviceKey);

        return builder;
    }

    /// <summary>
    /// Holds the consumer back until the authority is running, when the consumer supports waiting.
    /// </summary>
    /// <remarks>
    /// Tested at runtime rather than by a generic constraint, so the public methods above can return
    /// the caller's own builder type unchanged - which is what keeps them chainable with the
    /// component packages' own <c>WithReference</c> overloads.
    /// </remarks>
    private static void WaitForAuthority(
        IResourceBuilder<IResourceWithEnvironment> consumer,
        IResourceBuilder<ProjectResource> cloudLogin)
    {
        if (consumer.Resource is IResourceWithWaitSupport waitable)
            consumer.ApplicationBuilder.CreateResourceBuilder(waitable).WaitFor(cloudLogin);
    }

    private static CloudLoginClientOptions ClientOptions(Action<CloudLoginClientOptions>? configure)
    {
        CloudLoginClientOptions options = new();
        configure?.Invoke(options);
        return options;
    }

    internal static IResourceBuilder<ProjectResource> ApplyCloudLoginDefaults(this IResourceBuilder<ProjectResource> cloudLogin)
    {
        IDistributedApplicationBuilder builder = cloudLogin.ApplicationBuilder;

        if (builder.ExecutionContext.IsRunMode)
        {
            cloudLogin
                .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
                .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
                .WithEnvironment("CloudLogin:Security:RequireHttps", "false")
                .WithEnvironment("TestMode:IsEnabled", "true");
        }
        else
        {
            // Windows App Service runs without a loaded user profile unless asked, and CNG - which
            // is what ECDsa.Create() resolves to on Windows - then has nowhere to keep the key it
            // is handed. Importing the token signing key fails with "The system cannot find the
            // file specified", the token endpoint returns 500, and the person signing in is told by
            // the relying party that they were not found. Nothing in that chain names the cause.
            //
            // Harmless anywhere else: a platform that does not know this setting ignores it.
            cloudLogin.WithEnvironment("WEBSITE_LOAD_USER_PROFILE", "1");
        }

        // Where the user store and the file store live is the host's decision, made explicitly at
        // the AppHost with WithReference(cosmos) / WithReference(storage). Nothing is adopted or
        // created on its behalf: a CloudLogin server that is never pointed at an account is one
        // whose data keys stay unset, which is exactly what its own configuration model expects -
        // appsettings.json, a deployment tool, or a host that binds them itself.

        cloudLogin.WithEnvironment(
            CloudLoginConfigurationKeys.Tokens.Issuer,
            CloudLoginHostingExtensions.GetPreferredEndpoint(cloudLogin, null));

        return cloudLogin;
    }

    private static void ConfigureConsumer(
        IResourceBuilder<IResourceWithEnvironment> builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        string? endpointName,
        string configurationKey,
        CloudLoginClientOptions options)
    {
        string audience = builder.Resource.Name;
        CloudLoginServerAnnotation annotation =
            CloudLoginHostingExtensions.GetCloudLoginServer(cloudLogin, nameof(ConfigureConsumer));

        if (!annotation.AddConsumer(audience))
            return;

        EndpointReference authority = CloudLoginHostingExtensions.GetPreferredEndpoint(cloudLogin, endpointName);

        builder
            .WithEnvironment(configurationKey, authority)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.Authority, authority)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.Audience, audience)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.ClientId, audience);

        cloudLogin.Resource.Annotations.Add(new CloudLoginConsumerAnnotation(builder.Resource));

        PublishOwnAddress(builder, options);

        // Nothing about the application is written to the authority. A website with a backend gets the AppHost's one secret key, which the
        // authority accepts from any of them; the application appears in the authority's admin the first time it signs someone in.
        if (!options.IsPublic)
            builder.WithEnvironment(CloudLoginConfigurationKeys.Client.ClientSecret, SecretKey(cloudLogin, annotation));

        PublishDownstreamServices(builder, cloudLogin);
    }

    /// <summary>The authority's one generated secret key, declared to it once and shared by every website the AppHost references.</summary>
    private static IResourceBuilder<ParameterResource> SecretKey(IResourceBuilder<ProjectResource> cloudLogin, CloudLoginServerAnnotation annotation)
    {
        if (annotation.SecretKey is { } existing)
            return existing;

        IResourceBuilder<ParameterResource> key = SharedParameter(cloudLogin.ApplicationBuilder, $"{Sanitize(cloudLogin.Resource.Name)}-secret-key", generate: true);
        cloudLogin.WithEnvironment($"{CloudLoginConfigurationKeys.Tokens.SecretKeys}:0", key);
        annotation.SecretKey = key;
        return key;
    }

    /// <summary>One secret parameter per name, however many applications ask for it.</summary>
    private static IResourceBuilder<ParameterResource> SharedParameter(IDistributedApplicationBuilder application, string name, bool generate)
    {
        if (application.Resources.OfType<ParameterResource>().FirstOrDefault(parameter => parameter.Name == name) is { } existing)
            return application.CreateResourceBuilder(existing);

        return generate
            ? application.AddParameter(name, new GenerateParameterDefault { MinLength = 48, Lower = true, Upper = true, Numeric = true, Special = false, MinLower = 8, MinUpper = 8, MinNumeric = 8 }, secret: true, persist: false)
            : application.AddParameter(name, secret: true);
    }

    /// <summary>
    /// Tells this application which audience belongs to each service it references, so
    /// its outbound calls carry a token that service will actually accept.
    /// <para>
    /// A website with a key may ask for a token for any backend; without this list it does not
    /// know which audience a service expects, and sends its own token to a service that validates a
    /// different audience. That is rejected, the request arrives anonymous, and the
    /// receiving service answers 403 &mdash; a denial that looks nothing like a
    /// misconfigured audience.
    /// </para>
    /// <para>
    /// Evaluated lazily: sibling services register with the authority in whatever order
    /// the AppHost happens to declare them, and only at run or publish time is that list
    /// complete.
    /// </para>
    /// </summary>
    private static void PublishDownstreamServices(
        IResourceBuilder<IResourceWithEnvironment> builder,
        IResourceBuilder<ProjectResource> cloudLogin)
    {
        builder.WithEnvironment(context =>
        {
            // Only registered consumers have an audience of their own. A referenced
            // database or storage account is not something a user token is minted for.
            HashSet<string> consumerNames =
            [
                .. cloudLogin.Resource.Annotations
                    .OfType<CloudLoginConsumerAnnotation>()
                    .Select(consumer => consumer.Resource.Name)
            ];

            int index = 0;

            foreach (ResourceRelationshipAnnotation relationship in
                     builder.Resource.Annotations.OfType<ResourceRelationshipAnnotation>())
            {
                if (relationship.Resource is not IResourceWithEndpoints downstream ||
                    ReferenceEquals(downstream, builder.Resource) ||
                    string.Equals(downstream.Name, cloudLogin.Resource.Name, StringComparison.Ordinal) ||
                    !consumerNames.Contains(downstream.Name))
                    continue;

                context.EnvironmentVariables[
                    $"{CloudLoginConfigurationKeys.Client.DownstreamServices}:{index}:Audience"] = downstream.Name;

                context.EnvironmentVariables[
                    $"{CloudLoginConfigurationKeys.Client.DownstreamServices}:{index}:BaseUrl"] =
                    GetPreferredEndpoint(builder.ApplicationBuilder.CreateResourceBuilder(downstream));

                index++;
            }
        });
    }

    /// <summary>
    /// The application's own address, decided when the model is evaluated rather than when the reference is declared, so an
    /// endpoint added after <c>WithReference</c> still counts. An application with no endpoint has no address to publish.
    /// </summary>
    private static void PublishOwnAddress(IResourceBuilder<IResourceWithEnvironment> builder, CloudLoginClientOptions options)
    {
        if (builder.Resource is not IResourceWithEndpoints endpoints)
            return;

        builder.WithEnvironment(context =>
        {
            if (OwnAddress(builder.ApplicationBuilder, endpoints, options) is { } origin)
                context.EnvironmentVariables[CloudLoginConfigurationKeys.Client.PublicUrl] = origin;
        });
    }

    private static EndpointReference? OwnAddress(IDistributedApplicationBuilder application, IResourceWithEndpoints resource, CloudLoginClientOptions options)
    {
        if (resource.Annotations.OfType<EndpointAnnotation>().Any())
            return GetPreferredEndpoint(application.CreateResourceBuilder(resource));

        if (options.IsPublic)
            throw new DistributedApplicationException($"'{resource.Name}' is a public client, so it signs in through the browser and needs an endpoint to return to.");

        return null;
    }

    private static EndpointReference GetPreferredEndpoint<T>(IResourceBuilder<T> resource)
        where T : IResourceWithEndpoints
    {
        List<string> endpointNames =
        [
            .. resource.Resource.Annotations
                .OfType<EndpointAnnotation>()
                .Select(endpoint => endpoint.Name)
        ];

        if (endpointNames.Count == 0 || endpointNames.Contains("https", StringComparer.OrdinalIgnoreCase))
            return resource.GetEndpoint("https");

        if (endpointNames.Contains("http", StringComparer.OrdinalIgnoreCase))
            return resource.GetEndpoint("http");

        return resource.GetEndpoint(endpointNames[0]);
    }

    private static string Sanitize(string value)
    {
        string sanitized = new(value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-')
            .ToArray());

        return sanitized.Trim('-');
    }
}

/// <summary>Records a relying resource linked to a CloudLogin authority.</summary>
/// <param name="resource">The linked relying resource.</param>
public sealed class CloudLoginConsumerAnnotation(IResource resource) : IResourceAnnotation
{
    /// <summary>Gets the linked relying resource.</summary>
    public IResource Resource { get; } = resource;
}

