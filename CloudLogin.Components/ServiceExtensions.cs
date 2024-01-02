using AngryMonkey.CloudLogin;

namespace Microsoft.Extensions.DependencyInjection;
public static class MvcServiceCollectionExtensions
{
    public static async Task AddCloudLogin(this IServiceCollection services, string loginHttpServerUrl)
    {
        services.AddSingleton(await CloudLoginClient.Build(loginHttpServerUrl));
    }
}