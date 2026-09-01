using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>Persistence for the <c>Workspaces</c> container (partition key <c>/id</c>).</summary>
public interface IWorkspaceRepository
{
    Task<WorkspaceDocument?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> when the id already exists.</summary>
    Task CreateAsync(WorkspaceDocument workspace, CancellationToken cancellationToken = default);

    /// <summary>ETag-guarded replace. Throws <see cref="CoreConcurrencyException"/> on a lost race.</summary>
    Task ReplaceAsync(WorkspaceDocument workspace, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Cross-partition scan; service integrations only.</summary>
    Task<List<WorkspaceDocument>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence for the <c>WorkspaceAccess</c> container (partition key <c>/workspaceId</c>):
/// memberships (permanent, no ttl) and invitations (expiring, positive ttl).
/// </summary>
public interface IWorkspaceAccessRepository
{
    Task<WorkspaceAccessDocument?> GetAsync(Guid workspaceId, string accessId, CancellationToken cancellationToken = default);

    Task<List<WorkspaceAccessDocument>> GetAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> for an already-existing membership/invitation.</summary>
    Task CreateAsync(WorkspaceAccessDocument access, CancellationToken cancellationToken = default);

    /// <summary>ETag-guarded replace. Throws <see cref="CoreConcurrencyException"/> on a lost race.</summary>
    Task ReplaceAsync(WorkspaceAccessDocument access, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid workspaceId, string accessId, CancellationToken cancellationToken = default);

    /// <summary>Removes every access record of a workspace (workspace deletion).</summary>
    Task DeleteAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-partition query for a user's memberships. Authoritative; the optional
    /// <see cref="IUserWorkspaceIndexStore"/> only accelerates it.
    /// </summary>
    Task<List<WorkspaceAccessDocument>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an owner membership while ETag-touching every other active owner in the same
    /// transactional batch, preventing two concurrent demotions from each removing the other.
    /// </summary>
    async Task ReplaceWithOwnerGuardAsync(
        WorkspaceAccessDocument membership,
        IReadOnlyList<WorkspaceAccessDocument> activeOwners,
        CancellationToken cancellationToken = default) =>
        await ReplaceAsync(membership, cancellationToken);

    async Task DeleteWithOwnerGuardAsync(
        WorkspaceAccessDocument membership,
        IReadOnlyList<WorkspaceAccessDocument> activeOwners,
        CancellationToken cancellationToken = default) =>
        await DeleteAsync(Guid.Parse(membership.WorkspaceId), membership.Id, cancellationToken);

    /// <summary>Consumes an invitation and creates its membership in one partition transaction.</summary>
    async Task AcceptInvitationAsync(
        WorkspaceAccessDocument invitation,
        WorkspaceAccessDocument membership,
        CancellationToken cancellationToken = default)
    {
        await ReplaceAsync(invitation, cancellationToken);
        await CreateAsync(membership, cancellationToken);
    }
}
