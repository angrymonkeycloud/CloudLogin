using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>Configures CloudLogin through the same keys its project binds at runtime.</summary>
public static class CloudLoginProjectConfigurationExtensions
{
    /// <summary>Sets any CloudLogin runtime configuration value.</summary>
    public static CloudLoginProjectBuilder WithCloudLoginConfiguration(
        this CloudLoginProjectBuilder builder,
        string key,
        string value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        CloudLoginHostingExtensions.GetCloudLoginServer(builder.Inner, nameof(WithCloudLoginConfiguration)).Apply(key, value);
        builder.Inner.WithEnvironment(key, value);
        return builder;
    }

    /// <summary>Sets any CloudLogin runtime configuration value supplied by another Aspire resource.</summary>
    public static CloudLoginProjectBuilder WithCloudLoginConfiguration(
        this CloudLoginProjectBuilder builder,
        string key,
        IResourceBuilder<ParameterResource> value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        builder.Inner.WithEnvironment(key, value);
        return builder;
    }

    /// <summary>Sets the primary/accent color used by CloudLogin.</summary>
    public static CloudLoginProjectBuilder WithPrimaryColor(
        this CloudLoginProjectBuilder builder,
        string color) => builder.WithCloudLoginConfiguration(CloudLoginConfigurationKeys.PrimaryColor, color);

    /// <summary>Sets the CloudLogin page title.</summary>
    public static CloudLoginProjectBuilder WithCloudLoginTitle(
        this CloudLoginProjectBuilder builder,
        string title) => builder.WithCloudLoginConfiguration(CloudLoginConfigurationKeys.Title, title);

    /// <summary>Sets the CloudLogin Cosmos database and user container names.</summary>
    public static CloudLoginProjectBuilder WithCloudLoginDatabase(
        this CloudLoginProjectBuilder builder,
        string databaseId,
        string containerId = "Data") => builder
            .WithCloudLoginConfiguration(CloudLoginConfigurationKeys.Cosmos.DatabaseId, databaseId)
            .WithCloudLoginConfiguration(CloudLoginConfigurationKeys.Cosmos.ContainerId, containerId);

    /// <summary>Enables Microsoft sign-in using a protected client secret supplied by Aspire.</summary>
    public static CloudLoginProjectBuilder WithMicrosoftLogin(
        this CloudLoginProjectBuilder builder,
        string clientId,
        IResourceBuilder<ParameterResource> clientSecret,
        string label = "Microsoft") => builder
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.MicrosoftProvider}:ClientId", clientId)
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.MicrosoftProvider}:ClientSecret", clientSecret)
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.MicrosoftProvider}:Label", label);

    /// <summary>Enables Microsoft sign-in using client values supplied by another Aspire resource.</summary>
    public static CloudLoginProjectBuilder WithMicrosoftLogin(
        this CloudLoginProjectBuilder builder,
        IResourceBuilder<ParameterResource> clientId,
        IResourceBuilder<ParameterResource> clientSecret,
        string label = "Microsoft") => builder
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.MicrosoftProvider}:ClientId", clientId)
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.MicrosoftProvider}:ClientSecret", clientSecret)
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.MicrosoftProvider}:Label", label);

    /// <summary>Enables Google sign-in using a protected client secret supplied by Aspire.</summary>
    public static CloudLoginProjectBuilder WithGoogleLogin(
        this CloudLoginProjectBuilder builder,
        string clientId,
        IResourceBuilder<ParameterResource> clientSecret,
        string label = "Google") => builder
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.GoogleProvider}:ClientId", clientId)
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.GoogleProvider}:ClientSecret", clientSecret)
            .WithCloudLoginConfiguration($"{CloudLoginConfigurationKeys.GoogleProvider}:Label", label);
}
