using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

/// <summary>Shared Cosmos plumbing for the core repositories.</summary>
internal static class CosmosCoreOperations
{
    internal static async Task<T?> ReadOrNullAsync<T>(Container container, string id, PartitionKey partitionKey, CancellationToken cancellationToken) where T : class
    {
        try
        {
            ItemResponse<T> response = await container.ReadItemAsync<T>(id, partitionKey, cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    internal static async Task CreateOnlyAsync<T>(Container container, T item, PartitionKey partitionKey, CancellationToken cancellationToken)
    {
        try
        {
            await container.CreateItemAsync(item, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            throw new CoreConflictException("A record with the same id already exists.");
        }
    }

    internal static async Task ReplaceGuardedAsync<T>(Container container, T item, string id, PartitionKey partitionKey, string? etag, CancellationToken cancellationToken)
    {
        try
        {
            await container.ReplaceItemAsync(item, id, partitionKey,
                new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new CoreConcurrencyException("The record changed since it was read.");
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CoreConcurrencyException("The record no longer exists.");
        }
    }

    internal static async Task DeleteIfExistsAsync<T>(Container container, string id, PartitionKey partitionKey, CancellationToken cancellationToken)
    {
        try
        {
            await container.DeleteItemAsync<T>(id, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    internal static async Task<List<T>> QueryAsync<T>(Container container, QueryDefinition query, QueryRequestOptions? options, CancellationToken cancellationToken)
    {
        List<T> results = [];
        using FeedIterator<T> iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);

        while (iterator.HasMoreResults)
        {
            FeedResponse<T> response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }
}

public sealed class CosmosUserRepository(CosmosCoreDatabase database) : IUserRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.Users, cancellationToken);

    public async Task<UserDocument?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        string id = userId.ToString();
        return await CosmosCoreOperations.ReadOrNullAsync<UserDocument>(container, id, new PartitionKey(id), cancellationToken);
    }

    public async Task CreateAsync(UserDocument user, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.CreateOnlyAsync(container, user, new PartitionKey(user.Id), cancellationToken);
    }

    public async Task ReplaceAsync(UserDocument user, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.ReplaceGuardedAsync(container, user, user.Id, new PartitionKey(user.Id), user.ETag, cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        string id = userId.ToString();
        await CosmosCoreOperations.DeleteIfExistsAsync<UserDocument>(container, id, new PartitionKey(id), cancellationToken);
    }

    public async Task UpdateLastSignedInAsync(Guid userId, DateTimeOffset lastSignedIn, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        string id = userId.ToString();

        await container.PatchItemAsync<UserDocument>(id, new PartitionKey(id),
            [PatchOperation.Set("/LastSignedInOn", lastSignedIn)], cancellationToken: cancellationToken);
    }

    public async Task<List<UserDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new("SELECT * FROM c");
        return await CosmosCoreOperations.QueryAsync<UserDocument>(container, query, null, cancellationToken);
    }

    public async Task<List<UserDocument>> GetByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE UPPER(c.DisplayName) = UPPER(@displayName)")
            .WithParameter("@displayName", displayName);
        return await CosmosCoreOperations.QueryAsync<UserDocument>(container, query, null, cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new("SELECT VALUE COUNT(1) FROM c");
        List<int> counts = await CosmosCoreOperations.QueryAsync<int>(container, query, null, cancellationToken);
        return counts.Sum();
    }
}
