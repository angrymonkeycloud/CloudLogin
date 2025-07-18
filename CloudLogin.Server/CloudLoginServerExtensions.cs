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

        return services;
    }

    public static IServiceCollection AddCloudLoginServer(this IServiceCollection services, CloudLoginConfiguration configureOptions)
        => AddCloudLoginServer(services, config => config = configureOptions);
}
