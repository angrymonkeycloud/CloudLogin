using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.Server.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the modern storage core: the seven-container Cosmos database, the Table Storage
    /// identity index and the application services used by the CloudLogin authority.
    /// </summary>
    public static IServiceCollection AddCloudLoginCore(this IServiceCollection services, CloudLoginWebConfiguration configuration)
    {
        // No Cosmos account means no Cosmos storage to build a core over: a host running on its
        // own ICloudLoginStore (the demos, tests, an in-memory harness) keeps the store it
        // registered rather than having it replaced by an adapter with nothing to talk to. The
        // registered rather than replacing it with Azure-backed services.
        if (!configuration.Cosmos.IsValid())
            return services;

        CloudLoginCoreConfiguration core = configuration.Core;
        services.TryAddSingleton(core);
        services.TryAddSingleton(configuration.SignInProfiles);

        // ── Azure clients ────────────────────────────────────────────────────
        services.TryAddSingleton<CosmosCoreDatabase>(provider =>
        {
            CosmosClient client = provider.GetService<CosmosClient>() ?? configuration.Cosmos.CreateClient();
            return new CosmosCoreDatabase(client, core);
        });

        if (configuration.AzureStorage is { } storage && storage.IsValid())
        {
            // The identity index is keyed, so the key has to exist before anything can resolve a
            // sign-in. Built eagerly rather than lazily: a missing, malformed or weak secret must
            // stop the application starting, not surface as a failed sign-in later. Built only
            // when there is Azure storage to key, so a host with no identity table never needs it.
            services.TryAddSingleton(Domain.IdentityKeyHasher.FromConfiguredSecrets(
                configuration.IdentityHmacSecret,
                configuration.IdentityHmacFallbackSecrets));

            services.TryAddSingleton<TableServiceClient>(_ => storage.CreateTableServiceClient());
            services.TryAddSingleton<IIdentityKeyStore, TableIdentityKeyStore>();
            services.TryAddSingleton<IUserWorkspaceIndexStore, TableUserWorkspaceIndexStore>();
        }

        // ── Repositories ─────────────────────────────────────────────────────
        services.TryAddSingleton<IUserRepository, CosmosUserRepository>();
        services.TryAddSingleton<ICredentialRepository, CosmosCredentialRepository>();
        services.TryAddSingleton<IWorkspaceRepository, CosmosWorkspaceRepository>();
        services.TryAddSingleton<IWorkspaceAccessRepository, CosmosWorkspaceAccessRepository>();
        services.TryAddSingleton<ISessionRepository, CosmosSessionRepository>();
        services.TryAddSingleton<ILoginRequestRepository, CosmosLoginRequestRepository>();
        services.TryAddSingleton<IAuditEventRepository, CosmosAuditEventRepository>();

        // ── Application services ─────────────────────────────────────────────
        services.TryAddSingleton<IAuditLogger>(provider => new AuditLogger(
            provider.GetRequiredService<IAuditEventRepository>(), core,
            provider.GetService<ILogger<AuditLogger>>()));

        // Scoped where CloudGeographyClient may itself be scoped in the host.
        services.TryAddScoped<IdentityNormalization>();
        services.TryAddSingleton<IdentityLinkingService>();
        services.TryAddScoped<CoreUserService>();
        services.TryAddScoped<ICloudLoginSecurityStore, CoreSecurityStore>();
        services.TryAddSingleton<SessionService>();
        services.TryAddSingleton<DeviceAuthorizationService>();
        services.TryAddSingleton<SignInProfileService>();

        services.TryAddSingleton<WorkspaceAccessService>(provider => new WorkspaceAccessService(
            provider.GetRequiredService<IWorkspaceRepository>(),
            provider.GetRequiredService<IWorkspaceAccessRepository>(),
            provider.GetService<IUserWorkspaceIndexStore>(),
            core,
            provider.GetRequiredService<IAuditLogger>(),
            provider.GetRequiredService<IUserRepository>()));

        // ── Authority contract adapters over the split storage core ──────────
        services.RemoveAll<ICloudLoginStore>();
        services.TryAddScoped<CoreCloudLoginStoreAdapter>();
        services.AddScoped<ICloudLoginStore>(provider => provider.GetRequiredService<CoreCloudLoginStoreAdapter>());

        services.RemoveAll<ICloudLoginWorkspaceRegistry>();
        services.AddScoped<ICloudLoginWorkspaceRegistry, CoreWorkspaceRegistryAdapter>();

        // Registered before the token issuer's TryAdd, so refresh tokens and the Cosmos
        // signing-key fallback live in the core model too — no expiring security state
        // remains outside it.
        services.RemoveAll<Tokens.ICloudLoginTokenStore>();
        services.AddSingleton<Tokens.ICloudLoginTokenStore, CoreTokenStoreAdapter>();

        // Verification codes join the other short-lived, single-winner requests in the core model,
        // ahead of the in-process fallback AddCloudLoginWeb registers for hosts without a database.
        services.RemoveAll<Verification.ICloudLoginVerificationStore>();
        services.AddSingleton<Verification.ICloudLoginVerificationStore, CoreVerificationStore>();

        return services;
    }
}
