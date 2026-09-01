using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Azure;
using AngryMonkey.CloudLogin.Server.Versioning;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Server.Storage;

/// <summary>
/// Creates CloudLogin's own database and containers at startup.
/// <para>
/// CloudLogin owns its schema, so a deployment needs no separate provisioning step: this runs
/// identically whether the app is composed by an Aspire/CoconutSharp AppHost, started by
/// <c>dotnet run</c> against a connection string, or published to App Service. An AppHost that
/// also declares the same resources is harmless - every call here is create-if-not-exists.
/// </para>
/// <para>
/// Which schema is created follows <see cref="CloudLoginWebConfiguration.DatabaseVersion"/>:
/// V3 provisions the seven core containers with their TTL settings, V2 provisions the single
/// legacy container. Runs before the first request is served so a cold start never races
/// container creation.
/// </para>
/// </summary>
public sealed class CloudLoginStorageProvisioner(
    CloudLoginWebConfiguration configuration,
    IServiceProvider services,
    ILogger<CloudLoginStorageProvisioner>? logger = null) : IHostedService
{
    private readonly CloudLoginWebConfiguration _configuration = configuration;
    private readonly IServiceProvider _services = services;
    private readonly ILogger<CloudLoginStorageProvisioner>? _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.Cosmos.IsValid())
            return;

        try
        {
            if (_configuration.UsesCoreDatabase)
                await ProvisionCoreAsync(cancellationToken);
            else
                await ProvisionLegacyAsync(cancellationToken);
        }
        catch (CosmosException exception) when (
            exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            // Creating databases and containers is a Cosmos control-plane operation, which the
            // data-plane RBAC role a managed identity usually holds does not grant. That is a
            // supported deployment shape - the AppHost's own deployment credentials provision the
            // resources instead - so this is not a startup failure. If the containers genuinely
            // are missing, the first data call says so plainly.
            _logger?.LogInformation(
                exception,
                "CloudLogin could not create its {DatabaseVersion} storage (the running identity has no Cosmos " +
                "control-plane permission). Continuing: the containers are expected to be provisioned already.",
                _configuration.DatabaseVersion);
        }
        catch (Exception exception)
        {
            // Never block startup on provisioning: the repositories create what they need lazily
            // on first use, so a transient failure here must not take the authority down.
            _logger?.LogWarning(
                exception,
                "CloudLogin storage provisioning did not complete; it will be retried on first use.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ProvisionCoreAsync(CancellationToken cancellationToken)
    {
        CosmosCoreDatabase database = _services.GetRequiredService<CosmosCoreDatabase>();
        await database.ProvisionAllAsync(cancellationToken);

        _logger?.LogInformation(
            "CloudLogin storage ready: database '{DatabaseId}' with the {ContainerCount} core containers.",
            _configuration.Core!.DatabaseId,
            7);
    }

    private async Task ProvisionLegacyAsync(CancellationToken cancellationToken)
    {
        CosmosConfiguration cosmos = _configuration.Cosmos;

        // Validation guarantees both are named under V2.
        CosmosClient client = _services.GetService<CosmosClient>() ?? cosmos.CreateClient();

        Database database = (await client.CreateDatabaseIfNotExistsAsync(
            cosmos.DatabaseId, cancellationToken: cancellationToken)).Database;

        // The legacy schema keys every document type by the same discriminator partition, whose
        // path is itself configurable - so the container is created on whatever path this
        // deployment's documents actually carry.
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(cosmos.ContainerId, cosmos.PartitionKeyName),
            cancellationToken: cancellationToken);

        _logger?.LogInformation(
            "CloudLogin storage ready: legacy database '{DatabaseId}', container '{ContainerId}'.",
            cosmos.DatabaseId,
            cosmos.ContainerId);
    }
}

public static class CloudLoginStorageProvisioningExtensions
{
    /// <summary>
    /// Registers the startup provisioner that creates CloudLogin's database and containers.
    /// Idempotent across both hosts - the standalone site and an embedded host both call it.
    /// </summary>
    public static IServiceCollection AddCloudLoginStorageProvisioning(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ImplementationType == typeof(CloudLoginStorageProvisioner)))
            return services;

        services.AddHostedService<CloudLoginStorageProvisioner>();
        return services;
    }
}
