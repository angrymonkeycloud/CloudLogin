using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Server;

public static class CloudLoginServerExtensions
{
    public static IServiceCollection AddCloudLoginWeb(this IServiceCollection services, Action<CloudLoginWebConfiguration> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddHttpContextAccessor();

        services.AddSingleton<CloudGeographyClient>();
        services.AddSingleton<CloudLoginAuthenticationService>();
        services.AddScoped<ICloudLogin, CloudLoginServer>();
        services.AddScoped<CloudLoginServer>();

        return services;
    }

    public static IServiceCollection AddCloudLoginWeb(this IServiceCollection services, CloudLoginWebConfiguration configureOptions)
    {
        // Configure BaseRecord with Cosmos configuration for property naming
        BaseRecord.CosmosConfiguration = configureOptions.Cosmos;
        
        return AddCloudLoginWeb(services, config => config = configureOptions);
    }
}
