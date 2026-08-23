using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class AccountRegistryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the workspace, subscription, and billing registries. Workspace caps come
    /// from <c>CloudLoginWebConfiguration.Workspace</c>, so this may be called before or after
    /// <c>AddCloudLoginWeb</c> — the configuration is resolved per request, not at registration.
    /// </summary>
    public static IServiceCollection AddCloudLoginAccountRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<ICloudLoginAccountStore, InMemoryCloudLoginAccountStore>();
        services.TryAddScoped<ICloudLoginWorkspaceRegistry, WorkspaceRegistry>();
        services.TryAddScoped<ICloudLoginSubscriptionRegistry, SubscriptionRegistry>();
        return services;
    }
}
