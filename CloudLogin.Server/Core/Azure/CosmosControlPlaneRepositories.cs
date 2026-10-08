using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

public sealed class CosmosApplicationRepository(CosmosCoreDatabase database) : IApplicationRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.Applications, cancellationToken);

    public async Task<ApplicationDocument?> GetAsync(string clientId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.ReadOrNullAsync<ApplicationDocument>(container, clientId, new PartitionKey(clientId), cancellationToken);
    }

    public async Task<List<ApplicationDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.QueryAsync<ApplicationDocument>(container, new QueryDefinition("SELECT * FROM c"), null, cancellationToken);
    }

    public async Task CreateAsync(ApplicationDocument application, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.CreateOnlyAsync(container, application, new PartitionKey(application.Id), cancellationToken);
    }

    public async Task ReplaceAsync(ApplicationDocument application, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.ReplaceGuardedAsync(container, application, application.Id, new PartitionKey(application.Id), application.ETag, cancellationToken);
    }

    public async Task<bool> TryReplaceAsync(ApplicationDocument application, CancellationToken cancellationToken = default)
    {
        try
        {
            await ReplaceAsync(application, cancellationToken);
            return true;
        }
        catch (CoreConcurrencyException)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string clientId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.DeleteIfExistsAsync<ApplicationDocument>(container, clientId, new PartitionKey(clientId), cancellationToken);
    }
}

public sealed class CosmosSecretKeyRepository(CosmosCoreDatabase database) : ISecretKeyRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.SecretKeys, cancellationToken);

    public async Task<SecretKeyDocument?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.ReadOrNullAsync<SecretKeyDocument>(container, id, new PartitionKey(id), cancellationToken);
    }

    public async Task<List<SecretKeyDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.QueryAsync<SecretKeyDocument>(container, new QueryDefinition("SELECT * FROM c"), null, cancellationToken);
    }

    public async Task CreateAsync(SecretKeyDocument key, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.CreateOnlyAsync(container, key, new PartitionKey(key.Id), cancellationToken);
    }

    public async Task<bool> TryReplaceAsync(SecretKeyDocument key, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);

        try
        {
            await CosmosCoreOperations.ReplaceGuardedAsync(container, key, key.Id, new PartitionKey(key.Id), key.ETag, cancellationToken);
            return true;
        }
        catch (CoreConcurrencyException)
        {
            return false;
        }
    }
}
