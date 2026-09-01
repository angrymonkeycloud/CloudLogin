using System.Net;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

public sealed class CosmosLoginRequestRepository(CosmosCoreDatabase database) : ILoginRequestRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.LoginRequests, cancellationToken);

    public async Task<LoginRequestDocument?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        return await CosmosCoreOperations.ReadOrNullAsync<LoginRequestDocument>(container, requestId, new PartitionKey(requestId), cancellationToken);
    }

    public async Task CreateAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(request);
        await CosmosCoreOperations.CreateOnlyAsync(container, request, new PartitionKey(request.Id), cancellationToken);
    }

    public async Task<bool> TryReplaceAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(request);

        try
        {
            ItemResponse<LoginRequestDocument> response = await container.ReplaceItemAsync(
                request, request.Id, new PartitionKey(request.Id),
                new ItemRequestOptions { IfMatchEtag = request.ETag }, cancellationToken);

            request.ETag = response.ETag;
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> TryDeleteAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);

        try
        {
            await container.DeleteItemAsync<LoginRequestDocument>(request.Id, new PartitionKey(request.Id),
                new ItemRequestOptions { IfMatchEtag = request.ETag }, cancellationToken);
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<LoginRequestDocument?> FindByUserCodeHashAsync(string userCodeHash, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT TOP 2 * FROM c WHERE c.UserCodeHash = @hash")
            .WithParameter("@hash", userCodeHash);

        List<LoginRequestDocument> matches = await CosmosCoreOperations.QueryAsync<LoginRequestDocument>(container, query, null, cancellationToken);
        List<LoginRequestDocument> active = [.. matches.Where(match => !DocumentExpiry.IsExpired(match))];
        return active.Count == 1 ? active[0] : null;
    }
}

public sealed class CosmosAuditEventRepository(CosmosCoreDatabase database) : IAuditEventRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.AuditEvents, cancellationToken);

    public async Task AppendAsync(AuditEventDocument auditEvent, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(auditEvent);
        await container.CreateItemAsync(auditEvent, new PartitionKey(auditEvent.PartitionKey), cancellationToken: cancellationToken);
    }

    public async Task<List<AuditEventDocument>> GetPartitionAsync(string partitionKey, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT TOP @max * FROM c ORDER BY c.OccurredOn DESC")
            .WithParameter("@max", maxCount);
        QueryRequestOptions options = new() { PartitionKey = new PartitionKey(partitionKey) };

        return await CosmosCoreOperations.QueryAsync<AuditEventDocument>(container, query, options, cancellationToken);
    }
}
