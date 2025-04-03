using AngryMonkey.Cloud;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Server;

public static class CloudLoginServerExtensions
{
    public static IServiceCollection AddCloudLoginServer(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        
        services.AddSingleton<CloudGeographyClient>();
        
        services.AddScoped<CloudLoginServer>();

        return services;
    }

    public static IServiceCollection AddCloudLoginServer(this IServiceCollection services, Action<CloudLoginConfiguration> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddCloudLoginServer();

        return services;
    }
}
