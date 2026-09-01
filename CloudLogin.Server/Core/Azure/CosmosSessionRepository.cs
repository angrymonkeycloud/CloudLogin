using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

public sealed class CosmosSessionRepository(CosmosCoreDatabase database) : ISessionRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.Sessions, cancellationToken);

    public async Task<SessionFamilyDocument?> GetFamilyAsync(string familyId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.ReadOrNullAsync<SessionFamilyDocument>(container, familyId, new PartitionKey(familyId), cancellationToken);
    }

    public async Task<SessionTokenDocument?> GetTokenAsync(string familyId, string tokenId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.ReadOrNullAsync<SessionTokenDocument>(container, tokenId, new PartitionKey(familyId), cancellationToken);
    }

    public async Task CreateFamilyAsync(SessionFamilyDocument family, SessionTokenDocument firstToken, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);

        TransactionalBatch batch = container.CreateTransactionalBatch(new PartitionKey(family.FamilyId))
            .CreateItem(family)
            .CreateItem(firstToken);

        using TransactionalBatchResponse response = await batch.ExecuteAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new CoreConflictException($"Creating the session family failed with status {(int)response.StatusCode}.");
    }

    public async Task RotateAsync(SessionFamilyDocument family, SessionTokenDocument consumedToken, SessionTokenDocument newToken, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);

        // One atomic batch inside the family partition: consume the old token, create the new
        // one, advance the head. The ETags read before the batch guard every leg, so a parallel
        // exchange of the same token can never double-rotate.
        TransactionalBatch batch = container.CreateTransactionalBatch(new PartitionKey(family.FamilyId))
            .ReplaceItem(consumedToken.Id, consumedToken, new TransactionalBatchItemRequestOptions { IfMatchEtag = consumedToken.ETag })
            .CreateItem(newToken)
            .ReplaceItem(family.Id, family, new TransactionalBatchItemRequestOptions { IfMatchEtag = family.ETag });

        using TransactionalBatchResponse response = await batch.ExecuteAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new CoreConcurrencyException($"Refresh rotation lost a concurrency race (status {(int)response.StatusCode}).");
    }

    public async Task ReplaceFamilyAsync(SessionFamilyDocument family, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.ReplaceGuardedAsync(container, family, family.Id, new PartitionKey(family.FamilyId), family.ETag, cancellationToken);
    }

    public async Task<List<SessionFamilyDocument>> GetFamiliesForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.UserId = @userId AND c.Kind = 'Family'")
            .WithParameter("@userId", userId.ToString());

        List<SessionFamilyDocument> families = await CosmosCoreOperations.QueryAsync<SessionFamilyDocument>(container, query, null, cancellationToken);
        return [.. families.Where(family => !DocumentExpiry.IsExpired(family))];
    }

    public async Task<SessionTokenDocument?> FindTokenByIdAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.Kind = 'Token'")
            .WithParameter("@id", tokenId);

        List<SessionTokenDocument> matches = await CosmosCoreOperations.QueryAsync<SessionTokenDocument>(container, query, null, cancellationToken);
        return matches.FirstOrDefault();
    }

    public async Task<List<SessionFamilyDocument>> FindFamiliesBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.SessionId = @sessionId AND c.Kind = 'Family'")
            .WithParameter("@sessionId", sessionId);

        return await CosmosCoreOperations.QueryAsync<SessionFamilyDocument>(container, query, null, cancellationToken);
    }

    public async Task UpsertTokenAsync(SessionTokenDocument token, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(token);
        await container.UpsertItemAsync(token, new PartitionKey(token.FamilyId), cancellationToken: cancellationToken);
    }
}
