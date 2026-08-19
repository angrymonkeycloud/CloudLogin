using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class AccountRegistryServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLoginAccountRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<ICloudLoginAccountStore, InMemoryCloudLoginAccountStore>();
        services.TryAddScoped<IOrganizationRegistry, OrganizationRegistry>();
        services.TryAddScoped<ISubscriptionRegistry, SubscriptionRegistry>();
        return services;
    }
}
