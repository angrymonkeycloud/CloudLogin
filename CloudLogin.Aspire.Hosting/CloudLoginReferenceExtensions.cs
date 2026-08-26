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
    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<ProjectResource> cloudLogin)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        if (!cloudLogin.Resource.Annotations.OfType<CloudLoginServerAnnotation>().Any())
            return global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(builder, cloudLogin);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(builder, cloudLogin);
            builder.WaitFor(cloudLogin);
        }
        ConfigureConsumer(builder, cloudLogin, null, CloudLoginConfigurationKeys.LoginUrl);
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
    public static IResourceBuilder<T> WithCloudLogin<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        string? endpointName = null,
        string configurationKey = CloudLoginConfigurationKeys.LoginUrl)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cloudLogin);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            global::Aspire.Hosting.ResourceBuilderExtensions.WithReference(builder, cloudLogin);
            builder.WaitFor(cloudLogin);
        }
        ConfigureConsumer(builder, cloudLogin, endpointName, configurationKey);
        return builder;
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

        List<AzureCosmosDBResource> cosmosResources = [.. builder.Resources.OfType<AzureCosmosDBResource>()];
        IResourceBuilder<AzureCosmosDBResource>? cosmos = cosmosResources.Count switch
        {
            0 => builder.AddAzureCosmosDB($"{cloudLogin.Resource.Name}-cosmos"),
            1 => builder.CreateResourceBuilder(cosmosResources[0]),
            _ => null
        };

        List<AzureStorageResource> storageResources = [.. builder.Resources.OfType<AzureStorageResource>()];
        IResourceBuilder<AzureStorageResource>? storage = storageResources.Count switch
        {
            0 => builder.AddAzureStorage($"{cloudLogin.Resource.Name}-storage"),
            1 => builder.CreateResourceBuilder(storageResources[0]),
            _ => null
        };

        if (cosmos is not null)
        {
            CloudLoginServerAnnotation annotation =
                CloudLoginHostingExtensions.GetCloudLoginServer(cloudLogin, nameof(ApplyCloudLoginDefaults));
            CloudLoginHostingExtensions.WithCloudLoginCosmos(
                cloudLogin, cosmos, annotation.DatabaseId, annotation.ContainerId);
        }

        if (storage is not null)
            CloudLoginHostingExtensions.WithCloudLoginStorage(cloudLogin, storage);

        cloudLogin.WithEnvironment(
            CloudLoginConfigurationKeys.Tokens.Issuer,
            CloudLoginHostingExtensions.GetPreferredEndpoint(cloudLogin, null));

        return cloudLogin;
    }

    private static void ConfigureConsumer<T>(
        IResourceBuilder<T> builder,
        IResourceBuilder<ProjectResource> cloudLogin,
        string? endpointName,
        string configurationKey)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport, IResourceWithEndpoints
    {
        string audience = builder.Resource.Name;
        CloudLoginServerAnnotation annotation =
            CloudLoginHostingExtensions.GetCloudLoginServer(cloudLogin, nameof(ConfigureConsumer));

        if (!annotation.AddConsumer(audience))
            return;

        EndpointReference authority = CloudLoginHostingExtensions.GetPreferredEndpoint(cloudLogin, endpointName);
        EndpointReference origin = GetPreferredEndpoint(builder);
        string parameterName = Sanitize($"{cloudLogin.Resource.Name}-{audience}-client-secret");

        IResourceBuilder<ParameterResource> clientSecret = builder.ApplicationBuilder.AddParameter(
            parameterName,
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
            persist: true);

        builder
            .WithEnvironment(configurationKey, authority)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.Authority, authority)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.Audience, audience)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.ClientId, audience)
            .WithEnvironment(CloudLoginConfigurationKeys.Client.ClientSecret, clientSecret);

        int consumerIndex = cloudLogin.Resource.Annotations
            .OfType<CloudLoginConsumerAnnotation>()
            .Count();

        cloudLogin.Resource.Annotations.Add(new CloudLoginConsumerAnnotation(builder.Resource));

        cloudLogin
            .WithEnvironment($"{CloudLoginConfigurationKeys.Tokens.AllowedAudiences}:{consumerIndex}", audience)
            .WithEnvironment($"{CloudLoginConfigurationKeys.Tokens.ServiceClients}:{audience}:ClientId", audience)
            .WithEnvironment($"{CloudLoginConfigurationKeys.Tokens.ServiceClients}:{audience}:Audience", audience)
            .WithEnvironment($"{CloudLoginConfigurationKeys.Tokens.ServiceClients}:{audience}:ClientSecret", clientSecret)
            .WithEnvironment($"{CloudLoginConfigurationKeys.AllowedRedirectOrigins}:{consumerIndex}", origin);

        HashSet<string> allowedAudiences =
        [
            audience,
            .. builder.Resource.Annotations
                .OfType<ResourceRelationshipAnnotation>()
                .Select(relationship => relationship.Resource.Name)
                .Where(name => !string.Equals(name, cloudLogin.Resource.Name, StringComparison.Ordinal))
        ];

        int allowedIndex = 0;
        foreach (string allowedAudience in allowedAudiences.Order(StringComparer.Ordinal))
        {
            cloudLogin.WithEnvironment(
                $"{CloudLoginConfigurationKeys.Tokens.ServiceClients}:{audience}:AllowedAudiences:{allowedIndex++}",
                allowedAudience);
        }
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

