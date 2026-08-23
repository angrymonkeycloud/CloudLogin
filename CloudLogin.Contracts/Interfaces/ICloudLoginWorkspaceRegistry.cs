namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLoginWorkspaceRegistry
{
    Task<CloudWorkspace> CreateAsync(string name, Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<CloudWorkspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudWorkspace>> GetWorkspacesForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudWorkspace>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudWorkspaceMember>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<CloudWorkspaceMember> AddMemberAsync(Guid workspaceId, Guid userId, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default);
    Task<CloudWorkspaceInvitation> InviteAsync(Guid workspaceId, string recipient, Guid invitedByUserId, DateTimeOffset expiresAt, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates workspace profile fields (Name, contact details, and billing information).
    /// Throws <see cref="UnauthorizedAccessException"/> if <paramref name="callerUserId"/> is not the workspace's owner.
    /// </summary>
    Task<CloudWorkspace> UpdateAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>How many workspaces the user owns and belongs to, against the configured caps.</summary>
    Task<CloudWorkspaceQuota> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What currently prevents deleting a workspace, and what deletion would take with it.
    /// Throws <see cref="UnauthorizedAccessException"/> unless the caller owns the workspace.
    /// </summary>
    Task<CloudWorkspaceDeletionReport> GetDeletionReportAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a workspace along with its memberships, invitations, billing profile, and
    /// removable subscriptions. Throws <see cref="UnauthorizedAccessException"/> unless the caller
    /// owns it, and <see cref="CloudWorkspaceDeletionBlockedException"/> while any subscription
    /// still blocks deletion.
    /// </summary>
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default);
}
