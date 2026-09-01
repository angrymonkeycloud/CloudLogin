using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

public sealed class CosmosWorkspaceRepository(CosmosCoreDatabase database) : IWorkspaceRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.Workspaces, cancellationToken);

    public async Task<WorkspaceDocument?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        string id = workspaceId.ToString();
        return await CosmosCoreOperations.ReadOrNullAsync<WorkspaceDocument>(container, id, new PartitionKey(id), cancellationToken);
    }

    public async Task CreateAsync(WorkspaceDocument workspace, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.CreateOnlyAsync(container, workspace, new PartitionKey(workspace.Id), cancellationToken);
    }

    public async Task ReplaceAsync(WorkspaceDocument workspace, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.ReplaceGuardedAsync(container, workspace, workspace.Id, new PartitionKey(workspace.Id), workspace.ETag, cancellationToken);
    }

    public async Task DeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        string id = workspaceId.ToString();
        await CosmosCoreOperations.DeleteIfExistsAsync<WorkspaceDocument>(container, id, new PartitionKey(id), cancellationToken);
    }

    public async Task<List<WorkspaceDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new("SELECT * FROM c");
        return await CosmosCoreOperations.QueryAsync<WorkspaceDocument>(container, query, null, cancellationToken);
    }
}

public sealed class CosmosWorkspaceAccessRepository(CosmosCoreDatabase database) : IWorkspaceAccessRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.WorkspaceAccess, cancellationToken);

    public async Task<WorkspaceAccessDocument?> GetAsync(Guid workspaceId, string accessId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        WorkspaceAccessDocument? access = await CosmosCoreOperations.ReadOrNullAsync<WorkspaceAccessDocument>(
            container, accessId, new PartitionKey(workspaceId.ToString()), cancellationToken);

        return access is not null && DocumentExpiry.IsExpired(access) ? null : access;
    }

    public async Task<List<WorkspaceAccessDocument>> GetAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new("SELECT * FROM c");
        QueryRequestOptions options = new() { PartitionKey = new PartitionKey(workspaceId.ToString()) };

        List<WorkspaceAccessDocument> access = await CosmosCoreOperations.QueryAsync<WorkspaceAccessDocument>(container, query, options, cancellationToken);
        return [.. access.Where(record => !DocumentExpiry.IsExpired(record))];
    }

    public async Task CreateAsync(WorkspaceAccessDocument access, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(access);
        await CosmosCoreOperations.CreateOnlyAsync(container, access, new PartitionKey(access.WorkspaceId), cancellationToken);
    }

    public async Task ReplaceAsync(WorkspaceAccessDocument access, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(access);
        await CosmosCoreOperations.ReplaceGuardedAsync(container, access, access.Id, new PartitionKey(access.WorkspaceId), access.ETag, cancellationToken);
    }

    public async Task DeleteAsync(Guid workspaceId, string accessId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.DeleteIfExistsAsync<WorkspaceAccessDocument>(container, accessId, new PartitionKey(workspaceId.ToString()), cancellationToken);
    }

    public async Task DeleteAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new("SELECT * FROM c");
        QueryRequestOptions options = new() { PartitionKey = new PartitionKey(workspaceId.ToString()) };
        List<WorkspaceAccessDocument> all = await CosmosCoreOperations.QueryAsync<WorkspaceAccessDocument>(container, query, options, cancellationToken);

        foreach (WorkspaceAccessDocument access in all)
            await CosmosCoreOperations.DeleteIfExistsAsync<WorkspaceAccessDocument>(container, access.Id, new PartitionKey(access.WorkspaceId), cancellationToken);
    }

    public async Task<List<WorkspaceAccessDocument>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.UserId = @userId AND c.Kind = 'Membership'")
            .WithParameter("@userId", userId.ToString());

        return await CosmosCoreOperations.QueryAsync<WorkspaceAccessDocument>(container, query, null, cancellationToken);
    }

    public async Task ReplaceWithOwnerGuardAsync(
        WorkspaceAccessDocument membership,
        IReadOnlyList<WorkspaceAccessDocument> activeOwners,
        CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        TransactionalBatch batch = container.CreateTransactionalBatch(
            new PartitionKey(membership.WorkspaceId));

        foreach (WorkspaceAccessDocument owner in activeOwners.Where(owner => owner.Id != membership.Id))
            batch.ReplaceItem(owner.Id, owner,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = owner.ETag });

        batch.ReplaceItem(membership.Id, membership,
            new TransactionalBatchItemRequestOptions { IfMatchEtag = membership.ETag });
        using TransactionalBatchResponse response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new CoreConcurrencyException(
                $"Owner update lost a concurrency race (status {(int)response.StatusCode}).");
    }

    public async Task DeleteWithOwnerGuardAsync(
        WorkspaceAccessDocument membership,
        IReadOnlyList<WorkspaceAccessDocument> activeOwners,
        CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        TransactionalBatch batch = container.CreateTransactionalBatch(
            new PartitionKey(membership.WorkspaceId));

        foreach (WorkspaceAccessDocument owner in activeOwners.Where(owner => owner.Id != membership.Id))
            batch.ReplaceItem(owner.Id, owner,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = owner.ETag });

        batch.DeleteItem(membership.Id,
            new TransactionalBatchItemRequestOptions { IfMatchEtag = membership.ETag });
        using TransactionalBatchResponse response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new CoreConcurrencyException(
                $"Owner removal lost a concurrency race (status {(int)response.StatusCode}).");
    }

    public async Task AcceptInvitationAsync(
        WorkspaceAccessDocument invitation,
        WorkspaceAccessDocument membership,
        CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(invitation);
        TransactionalBatch batch = container.CreateTransactionalBatch(
                new PartitionKey(invitation.WorkspaceId))
            .ReplaceItem(invitation.Id, invitation,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = invitation.ETag })
            .CreateItem(membership);

        using TransactionalBatchResponse response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new CoreConcurrencyException(
                $"Invitation acceptance lost a concurrency race (status {(int)response.StatusCode}).");
    }
}
