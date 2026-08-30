using System.Collections.Concurrent;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.Server.Services;

public sealed class InMemoryCloudLoginAccountStore : ICloudLoginAccountStore
{
    private readonly ConcurrentDictionary<Guid, CloudWorkspace> _workspaces = new();
    private readonly ConcurrentDictionary<(Guid WorkspaceId, Guid UserId), CloudWorkspaceMember> _members = new();
    private readonly ConcurrentDictionary<Guid, CloudWorkspaceInvitation> _invitations = new();

    public Task<CloudWorkspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(_workspaces.TryGetValue(workspaceId, out CloudWorkspace? workspace) ? workspace : null);
    public Task SaveWorkspaceAsync(CloudWorkspace workspace, CancellationToken cancellationToken = default) { _workspaces[workspace.ID] = workspace; return Task.CompletedTask; }
    public Task<IReadOnlyList<CloudWorkspace>> GetWorkspacesForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IEnumerable<Guid> workspaceIds = _members.Values.Where(member => member.UserId == userId).Select(member => member.WorkspaceId).Distinct();
        return Task.FromResult<IReadOnlyList<CloudWorkspace>>([.. workspaceIds.Select(id => _workspaces.TryGetValue(id, out CloudWorkspace? workspace) ? workspace : null).OfType<CloudWorkspace>()]);
    }
    public Task<IReadOnlyList<CloudWorkspace>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudWorkspace>>([.. _workspaces.Values]);
    public Task<IReadOnlyList<CloudWorkspaceMember>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudWorkspaceMember>>([.. _members.Values.Where(member => member.WorkspaceId == workspaceId)]);
    public Task SaveMemberAsync(CloudWorkspaceMember member, CancellationToken cancellationToken = default) { _members[(member.WorkspaceId, member.UserId)] = member; return Task.CompletedTask; }
    public Task SaveInvitationAsync(CloudWorkspaceInvitation invitation, CancellationToken cancellationToken = default) { _invitations[invitation.ID] = invitation; return Task.CompletedTask; }
    public Task<IReadOnlyList<CloudWorkspaceInvitation>> GetInvitationsAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudWorkspaceInvitation>>([.. _invitations.Values.Where(invitation => invitation.WorkspaceId == workspaceId)]);

    public Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) { _workspaces.TryRemove(workspaceId, out _); return Task.CompletedTask; }
    public Task DeleteMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) { _members.TryRemove((workspaceId, userId), out _); return Task.CompletedTask; }
    public Task DeleteInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default) { _invitations.TryRemove(invitationId, out _); return Task.CompletedTask; }
}

public sealed class WorkspaceRegistry(
    ICloudLoginAccountStore store,
    ICloudLoginEventPublisher? eventPublisher = null,
    CloudLoginWebConfiguration? configuration = null) : ICloudLoginWorkspaceRegistry
{
    private WorkspaceConfiguration Options => configuration?.Workspace ?? new WorkspaceConfiguration();
    private string SingularLabel => Options.SingularLabel.ToLowerInvariant();
    private string PluralLabel => Options.PluralLabel.ToLowerInvariant();

    public async Task<CloudWorkspace> CreateAsync(string name, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CloudWorkspaceQuota quota = await GetQuotaAsync(ownerUserId, cancellationToken);

        if (quota.RemainingOwned <= 0)
            throw new CloudWorkspaceLimitReachedException(CloudWorkspaceLimitKinds.Owned, quota.MaxOwned, SingularLabel, PluralLabel);

        if (quota.RemainingTotal <= 0)
            throw new CloudWorkspaceLimitReachedException(CloudWorkspaceLimitKinds.Membership, quota.MaxTotal, SingularLabel, PluralLabel);

        CloudWorkspace workspace = new() { Name = name.Trim(), OwnerUserId = ownerUserId };
        CloudWorkspaceMember owner = new() { WorkspaceId = workspace.ID, UserId = ownerUserId, IsOwner = true, Roles = ["Owner"] };
        await store.SaveWorkspaceAsync(workspace, cancellationToken);
        await store.SaveMemberAsync(owner, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Workspace.Created",
                "Workspace",
                workspace.ID,
                "Created",
                new { workspace.ID, workspace.OwnerUserId }),
                cancellationToken);
        return workspace;
    }

    public Task<CloudWorkspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => store.GetWorkspaceAsync(workspaceId, cancellationToken);
    public Task<IReadOnlyList<CloudWorkspace>> GetWorkspacesForUserAsync(Guid userId, CancellationToken cancellationToken = default) => store.GetWorkspacesForUserAsync(userId, cancellationToken);
    public Task<IReadOnlyList<CloudWorkspace>> GetAllAsync(CancellationToken cancellationToken = default) => store.GetAllWorkspacesAsync(cancellationToken);
    public Task<IReadOnlyList<CloudWorkspaceMember>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default) => store.GetMembersAsync(workspaceId, cancellationToken);

    public async Task<CloudWorkspaceQuota> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CloudWorkspace> workspaces = await store.GetWorkspacesForUserAsync(userId, cancellationToken);
        WorkspaceConfiguration options = Options;

        return new CloudWorkspaceQuota
        {
            Owned = workspaces.Count(workspace => workspace.OwnerUserId == userId),
            MaxOwned = options.EffectiveMaxOwnedPerUser,
            Total = workspaces.Count,
            MaxTotal = options.EffectiveMaxPerUser
        };
    }

    public async Task<CloudWorkspaceMember> AddMemberAsync(Guid workspaceId, Guid userId, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default)
    {
        _ = await store.GetWorkspaceAsync(workspaceId, cancellationToken) ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");

        // Joining counts against the member's own membership cap. An existing member being
        // re-saved with new roles isn't a new membership, so it never trips the cap.
        IReadOnlyList<CloudWorkspaceMember> existingMembers = await store.GetMembersAsync(workspaceId, cancellationToken);

        if (!existingMembers.Any(member => member.UserId == userId))
        {
            CloudWorkspaceQuota quota = await GetQuotaAsync(userId, cancellationToken);

            if (!quota.CanJoin)
                throw new CloudWorkspaceLimitReachedException(CloudWorkspaceLimitKinds.Membership, quota.MaxTotal, SingularLabel, PluralLabel);
        }

        CloudWorkspaceMember member = new() { WorkspaceId = workspaceId, UserId = userId, Roles = roles ?? [] };
        await store.SaveMemberAsync(member, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Workspace.MembershipUpdated",
                "Workspace",
                workspaceId,
                "MembershipUpdated",
                new { member.WorkspaceId, member.UserId, member.State }),
                cancellationToken);
        return member;
    }

    public async Task<CloudWorkspaceInvitation> InviteAsync(Guid workspaceId, string recipient, Guid invitedByUserId, DateTimeOffset expiresOn, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        if (expiresOn <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresOn));
        _ = await store.GetWorkspaceAsync(workspaceId, cancellationToken) ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");
        CloudWorkspaceInvitation invitation = new() { WorkspaceId = workspaceId, Recipient = recipient.Trim(), InvitedByUserId = invitedByUserId, ExpiresOn = expiresOn, Roles = roles ?? [] };
        await store.SaveInvitationAsync(invitation, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Workspace.InvitationCreated",
                "Workspace",
                workspaceId,
                "InvitationCreated",
                new { invitation.ID, invitation.WorkspaceId }),
                cancellationToken);
        return invitation;
    }

    public async Task<CloudWorkspace> UpdateAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        CloudWorkspace existing = await store.GetWorkspaceAsync(workspace.ID, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{workspace.ID}' was not found.");

        if (!await CanManageAsync(existing, callerUserId, cancellationToken))
            throw new UnauthorizedAccessException("Only the workspace's owner or an admin member may update its profile.");

        ArgumentException.ThrowIfNullOrWhiteSpace(workspace.Name);
        existing.Name = workspace.Name.Trim();
        existing.LegalName = Clean(workspace.LegalName);
        existing.Website = Clean(workspace.Website);
        existing.Phone = Clean(workspace.Phone);
        existing.BillingEmail = Clean(workspace.BillingEmail);
        existing.BillingContactName = Clean(workspace.BillingContactName);
        existing.TaxId = Clean(workspace.TaxId);
        existing.BillingAddress = new CloudWorkspaceAddress
        {
            Line1 = Clean(workspace.BillingAddress?.Line1),
            Line2 = Clean(workspace.BillingAddress?.Line2),
            City = Clean(workspace.BillingAddress?.City),
            State = Clean(workspace.BillingAddress?.State),
            PostalCode = Clean(workspace.BillingAddress?.PostalCode),
            Country = Clean(workspace.BillingAddress?.Country)
        };
        existing.UpdatedOn = DateTimeOffset.UtcNow;

        await store.SaveWorkspaceAsync(existing, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Workspace.Updated",
                "Workspace",
                existing.ID,
                "Updated",
                new { existing.ID, existing.OwnerUserId }),
                cancellationToken);
        return existing;
    }

    public async Task<CloudWorkspaceDeletionReport> GetDeletionReportAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        CloudWorkspace workspace = await store.GetWorkspaceAsync(workspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");

        if (!await IsOwnerAsync(workspace, callerUserId, cancellationToken))
            throw new UnauthorizedAccessException("Only the workspace's owner may delete it.");

        // Commercial records live in the applications now — CloudLogin holds nothing that could
        // block deletion, so the report only tells the owner who else loses access.
        IReadOnlyList<CloudWorkspaceMember> members = await store.GetMembersAsync(workspaceId, cancellationToken);

        return new CloudWorkspaceDeletionReport
        {
            WorkspaceId = workspaceId,
            Blockers = CloudWorkspaceDeletionBlockers.None,
            OtherMemberCount = members.Count(member => member.UserId != callerUserId),
            Reasons = []
        };
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        _ = await GetDeletionReportAsync(workspaceId, callerUserId, cancellationToken);

        // Clear the workspace's own records before the workspace itself: a store that fails
        // midway leaves a workspace the owner can retry, rather than orphaned members no one
        // can reach.
        foreach (CloudWorkspaceInvitation invitation in await store.GetInvitationsAsync(workspaceId, cancellationToken))
            await store.DeleteInvitationAsync(invitation.ID, cancellationToken);

        foreach (CloudWorkspaceMember member in await store.GetMembersAsync(workspaceId, cancellationToken))
            await store.DeleteMemberAsync(workspaceId, member.UserId, cancellationToken);

        await store.DeleteWorkspaceAsync(workspaceId, cancellationToken);

        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Workspace.Deleted",
                "Workspace",
                workspaceId,
                "Deleted",
                new { Id = workspaceId, DeletedByUserId = callerUserId }),
                cancellationToken);
    }

    private async Task<bool> IsOwnerAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (workspace.OwnerUserId == callerUserId)
            return true;

        IReadOnlyList<CloudWorkspaceMember> members = await store.GetMembersAsync(workspace.ID, cancellationToken);
        CloudWorkspaceMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);

        return caller is { IsOwner: true } || (caller?.Roles.Contains("Owner", StringComparer.OrdinalIgnoreCase) ?? false);
    }

    private async Task<bool> CanManageAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (await IsOwnerAsync(workspace, callerUserId, cancellationToken))
            return true;

        IReadOnlyList<CloudWorkspaceMember> members = await store.GetMembersAsync(workspace.ID, cancellationToken);
        CloudWorkspaceMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);

        return caller?.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) ?? false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
