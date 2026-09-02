using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AngryMonkey.CloudLogin.Server;

public static class CloudLoginServerExtensions
{
    public static IServiceCollection AddCloudLoginWeb(
        this IServiceCollection services,
        Action<CloudLoginWebConfiguration> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        CloudLoginWebConfiguration configuration = new();
        configureOptions(configuration);
        services.Configure(configureOptions);

        return RegisterServices(services, configuration);
    }

    public static IServiceCollection AddCloudLoginWeb(
        this IServiceCollection services,
        CloudLoginWebConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure CloudLoginBaseRecord with Cosmos configuration for property naming.
        CloudLoginBaseRecord.CosmosConfiguration = configuration.Cosmos;

        return RegisterServices(services, configuration);
    }

    private static IServiceCollection RegisterServices(
        IServiceCollection services,
        CloudLoginWebConfiguration configuration)
    {
        services.TryAddSingleton(configuration);
        services.AddHttpContextAccessor();
        services.TryAddSingleton<CloudGeographyClient>();
        services.TryAddSingleton<CloudLoginAuthenticationService>();
        services.TryAddScoped<ICloudLogin, CloudLoginServer>();
        services.TryAddScoped<CloudLoginServer>();
        services.AddHttpClient<ICloudLoginEventPublisher, CloudLoginWebhookPublisher>();

        // The fallback for a host with no core database. A core-enabled deployment replaces it with
        // the shared store, which is the only one correct across more than one instance.
        services.TryAddSingleton<Verification.ICloudLoginVerificationStore, Verification.InMemoryVerificationStore>();

        return services;
    }
}
