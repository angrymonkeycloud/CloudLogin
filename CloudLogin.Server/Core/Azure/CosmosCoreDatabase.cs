using AngryMonkey.CloudLogin.Server.Core;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

/// <summary>
/// Owns the core Cosmos database and its seven containers, provisioning them once on first use.
/// <para>
/// Containers that hold expiring documents (Credentials, WorkspaceAccess, Sessions,
/// LoginRequests, AuditEvents) are provisioned with <c>DefaultTimeToLive = -1</c>: TTL is
/// enabled at container level but no document expires unless it carries its own positive
/// <c>ttl</c>. Users and Workspaces never hold expiring documents and get no TTL at all.
/// </para>
/// </summary>
public sealed class CosmosCoreDatabase(CosmosClient client, CloudLoginCoreConfiguration configuration)
{
    private readonly CosmosClient _client = client;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;

    private readonly SemaphoreSlim _provisionLock = new(1, 1);
    private Database? _database;
    private readonly Dictionary<string, Container> _containers = [];

    /// <summary>
    /// Every container this database owns. Whether each carries TTL comes from
    /// <see cref="CloudLoginCoreContainers.RequiresTimeToLive"/> - the same list the Aspire
    /// hosting integration reads when it declares these in bicep.
    /// </summary>
    private static readonly (string Name, string PartitionKeyPath)[] ContainerDefinitions =
    [
        (CloudLoginCoreContainers.Users, CloudLoginCoreContainers.UsersPartitionKey),
        (CloudLoginCoreContainers.Credentials, CloudLoginCoreContainers.CredentialsPartitionKey),
        (CloudLoginCoreContainers.Workspaces, CloudLoginCoreContainers.WorkspacesPartitionKey),
        (CloudLoginCoreContainers.WorkspaceAccess, CloudLoginCoreContainers.WorkspaceAccessPartitionKey),
        (CloudLoginCoreContainers.Sessions, CloudLoginCoreContainers.SessionsPartitionKey),
        (CloudLoginCoreContainers.LoginRequests, CloudLoginCoreContainers.LoginRequestsPartitionKey),
        (CloudLoginCoreContainers.AuditEvents, CloudLoginCoreContainers.AuditEventsPartitionKey),

        // The optional signing-key fallback. Not one of the seven core containers and not part
        // of ProvisionAllAsync: it is created only when a deployment explicitly keeps its
        // signing keys in Cosmos instead of Key Vault. Retired material expires through TTL.
        (CloudLoginCoreContainers.SigningKeysFallback, CloudLoginCoreContainers.SigningKeysFallbackPartitionKey)
    ];

    public async Task<Container> GetContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        if (_containers.TryGetValue(containerName, out Container? existing))
            return existing;

        await _provisionLock.WaitAsync(cancellationToken);

        try
        {
            if (_containers.TryGetValue(containerName, out existing))
                return existing;

            _database ??= (await _client.CreateDatabaseIfNotExistsAsync(_configuration.DatabaseId, cancellationToken: cancellationToken)).Database;

            (string name, string partitionKeyPath) = ContainerDefinitions.First(definition =>
                string.Equals(definition.Name, containerName, StringComparison.Ordinal));

            bool enableTtl = CloudLoginCoreContainers.RequiresTimeToLive(name);
            ContainerProperties properties = new(name, partitionKeyPath);

            if (enableTtl)
                properties.DefaultTimeToLive = -1;

            ContainerResponse response = await _database.CreateContainerIfNotExistsAsync(properties, cancellationToken: cancellationToken);
            Container container = response.Container;

            // A container provisioned elsewhere (an AppHost, an ARM template) may exist without
            // TTL armed — and Cosmos silently ignores per-item ttl when container TTL is off, so
            // expiring security records would never be deleted. Repair rather than trust.
            if (enableTtl && response.Resource.DefaultTimeToLive != -1)
            {
                response.Resource.DefaultTimeToLive = -1;
                await container.ReplaceContainerAsync(response.Resource, cancellationToken: cancellationToken);
            }

            _containers[name] = container;
            return container;
        }
        finally
        {
            _provisionLock.Release();
        }
    }

    /// <summary>Provisions the seven core containers up front (startup validation and the migration tool use this).</summary>
    public async Task ProvisionAllAsync(CancellationToken cancellationToken = default)
    {
        foreach ((string name, _) in ContainerDefinitions)
        {
            if (string.Equals(name, CloudLoginCoreContainers.SigningKeysFallback, StringComparison.Ordinal))
                continue; // Only created on demand, when the fallback is explicitly in use.

            await GetContainerAsync(name, cancellationToken);
        }
    }
}
