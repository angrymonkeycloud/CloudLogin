namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLoginAccountStore
{
    Task<CloudWorkspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task SaveWorkspaceAsync(CloudWorkspace workspace, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudWorkspace>> GetWorkspacesForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudWorkspace>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudWorkspaceMember>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task SaveMemberAsync(CloudWorkspaceMember member, CancellationToken cancellationToken = default);
    Task SaveInvitationAsync(CloudWorkspaceInvitation invitation, CancellationToken cancellationToken = default);

    // ── Removal ───────────────────────────────────────────────────────────────
    // Stores written before workspace deletion existed keep compiling: each member below
    // reports that the store can't remove records rather than silently leaving them behind.
    // Implement all of them to let owners delete their workspaces.

    /// <summary>Invitations issued for a workspace, expired ones included.</summary>
    Task<IReadOnlyList<CloudWorkspaceInvitation>> GetInvitationsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement invitation reads.");

    Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");

    Task DeleteMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");

    Task DeleteInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");
}
