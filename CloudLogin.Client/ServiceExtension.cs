using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;
using Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static void AddCloudLogin(this IServiceCollection services, string loginServerUrl)
    {
        services.AddSingleton(sp => new CloudLoginClient
        {
            HttpServer = new() { BaseAddress = new(loginServerUrl) }
        });

        services.AddSingleton<ICloudLogin>(sp => sp.GetRequiredService<CloudLoginClient>());
    }
}
