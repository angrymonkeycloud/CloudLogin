using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class AccountRegistryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the organization, subscription, and billing registries. Organization caps come
    /// from <c>CloudLoginWebConfiguration.Organization</c>, so this may be called before or after
    /// <c>AddCloudLoginWeb</c> — the configuration is resolved per request, not at registration.
    /// </summary>
    public static IServiceCollection AddCloudLoginAccountRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<ICloudLoginAccountStore, InMemoryCloudLoginAccountStore>();
        services.TryAddScoped<IOrganizationRegistry, OrganizationRegistry>();
        services.TryAddScoped<ISubscriptionRegistry, SubscriptionRegistry>();
        return services;
    }
}
