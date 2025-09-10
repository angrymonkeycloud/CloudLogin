using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Server;

public static class CloudLoginServerExtensions
{
    public static IServiceCollection AddCloudLoginServer(this IServiceCollection services, Action<CloudLoginConfiguration> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddHttpContextAccessor();

        services.AddSingleton<CloudGeographyClient>();
        services.AddSingleton<CloudLoginAuthenticationService>();
        services.AddScoped<ICloudLogin, CloudLoginServer>();
        services.AddScoped<CloudLoginServer>();

        return services;
    }

    public static IServiceCollection AddCloudLoginServer(this IServiceCollection services, CloudLoginConfiguration configureOptions)
    {
        // Configure BaseRecord with Cosmos configuration for property naming
        BaseRecord.CosmosConfiguration = configureOptions.Cosmos;
        
        return AddCloudLoginServer(services, config => config = configureOptions);
    }
}
