using System.Collections.Concurrent;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.Server.Services;

public sealed class InMemoryCloudLoginAccountStore : ICloudLoginAccountStore
{
    private readonly ConcurrentDictionary<Guid, CloudWorkspace> _workspaces = new();
    private readonly ConcurrentDictionary<(Guid WorkspaceId, Guid UserId), CloudWorkspaceMember> _members = new();
    private readonly ConcurrentDictionary<Guid, CloudWorkspaceInvitation> _invitations = new();
    private readonly ConcurrentDictionary<Guid, CloudSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<(Guid? UserId, Guid? WorkspaceId), CloudBillingProfile> _billingProfiles = new();

    public Task<CloudWorkspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(_workspaces.TryGetValue(workspaceId, out CloudWorkspace? workspace) ? workspace : null);
    public Task SaveWorkspaceAsync(CloudWorkspace workspace, CancellationToken cancellationToken = default) { _workspaces[workspace.Id] = workspace; return Task.CompletedTask; }
    public Task<IReadOnlyList<CloudWorkspace>> GetWorkspacesForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IEnumerable<Guid> workspaceIds = _members.Values.Where(member => member.UserId == userId).Select(member => member.WorkspaceId).Distinct();
        return Task.FromResult<IReadOnlyList<CloudWorkspace>>([.. workspaceIds.Select(id => _workspaces.TryGetValue(id, out CloudWorkspace? workspace) ? workspace : null).OfType<CloudWorkspace>()]);
    }
    public Task<IReadOnlyList<CloudWorkspace>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudWorkspace>>([.. _workspaces.Values]);
    public Task<IReadOnlyList<CloudWorkspaceMember>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudWorkspaceMember>>([.. _members.Values.Where(member => member.WorkspaceId == workspaceId)]);
    public Task SaveMemberAsync(CloudWorkspaceMember member, CancellationToken cancellationToken = default) { _members[(member.WorkspaceId, member.UserId)] = member; return Task.CompletedTask; }
    public Task SaveInvitationAsync(CloudWorkspaceInvitation invitation, CancellationToken cancellationToken = default) { _invitations[invitation.Id] = invitation; return Task.CompletedTask; }
    public Task<IReadOnlyList<CloudWorkspaceInvitation>> GetInvitationsAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudWorkspaceInvitation>>([.. _invitations.Values.Where(invitation => invitation.WorkspaceId == workspaceId)]);
    public Task<CloudSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(_subscriptions.TryGetValue(subscriptionId, out CloudSubscription? subscription) ? subscription : null);
    public Task<IReadOnlyList<CloudSubscription>> GetSubscriptionsAsync(Guid? userId, Guid? workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudSubscription>>([.. _subscriptions.Values.Where(subscription => (userId is null || subscription.UserId == userId) && (workspaceId is null || subscription.WorkspaceId == workspaceId))]);
    public Task<IReadOnlyList<CloudSubscription>> GetAllSubscriptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudSubscription>>([.. _subscriptions.Values]);
    public Task SaveSubscriptionAsync(CloudSubscription subscription, CancellationToken cancellationToken = default) { _subscriptions[subscription.Id] = subscription; return Task.CompletedTask; }
    public Task<CloudBillingProfile?> GetBillingProfileAsync(Guid? userId, Guid? workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(_billingProfiles.TryGetValue((userId, workspaceId), out CloudBillingProfile? profile) ? profile : null);
    public Task SaveBillingProfileAsync(CloudBillingProfile profile, CancellationToken cancellationToken = default) { _billingProfiles[(profile.UserId, profile.WorkspaceId)] = profile; return Task.CompletedTask; }

    public Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) { _workspaces.TryRemove(workspaceId, out _); return Task.CompletedTask; }
    public Task DeleteMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) { _members.TryRemove((workspaceId, userId), out _); return Task.CompletedTask; }
    public Task DeleteInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default) { _invitations.TryRemove(invitationId, out _); return Task.CompletedTask; }
    public Task DeleteSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) { _subscriptions.TryRemove(subscriptionId, out _); return Task.CompletedTask; }
    public Task DeleteBillingProfileAsync(Guid? userId, Guid? workspaceId, CancellationToken cancellationToken = default) { _billingProfiles.TryRemove((userId, workspaceId), out _); return Task.CompletedTask; }
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
        CloudWorkspaceMember owner = new() { WorkspaceId = workspace.Id, UserId = ownerUserId, IsOwner = true, Roles = ["Owner"] };
        await store.SaveWorkspaceAsync(workspace, cancellationToken);
        await store.SaveMemberAsync(owner, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Workspace.Created",
                "Workspace",
                workspace.Id,
                "Created",
                new { workspace.Id, workspace.OwnerUserId }),
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
                new { invitation.Id, invitation.WorkspaceId }),
                cancellationToken);
        return invitation;
    }

    public async Task<CloudWorkspace> UpdateAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        CloudWorkspace existing = await store.GetWorkspaceAsync(workspace.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{workspace.Id}' was not found.");

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
                existing.Id,
                "Updated",
                new { existing.Id, existing.OwnerUserId }),
                cancellationToken);
        return existing;
    }

    public async Task<CloudWorkspaceDeletionReport> GetDeletionReportAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        CloudWorkspace workspace = await store.GetWorkspaceAsync(workspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");

        if (!await IsOwnerAsync(workspace, callerUserId, cancellationToken))
            throw new UnauthorizedAccessException("Only the workspace's owner may delete it.");

        return await BuildDeletionReportAsync(workspaceId, callerUserId, cancellationToken);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        CloudWorkspaceDeletionReport report = await GetDeletionReportAsync(workspaceId, callerUserId, cancellationToken);

        if (!report.CanDelete)
            throw new CloudWorkspaceDeletionBlockedException(report, SingularLabel);

        // Nothing here blocks any more, so clear the workspace's own records before the
        // workspace itself: a store that fails midway leaves a workspace the owner can
        // retry, rather than orphaned members and subscriptions no one can reach.
        foreach (CloudSubscription subscription in await store.GetSubscriptionsAsync(null, workspaceId, cancellationToken))
            await store.DeleteSubscriptionAsync(subscription.Id, cancellationToken);

        await store.DeleteBillingProfileAsync(null, workspaceId, cancellationToken);

        foreach (CloudWorkspaceInvitation invitation in await store.GetInvitationsAsync(workspaceId, cancellationToken))
            await store.DeleteInvitationAsync(invitation.Id, cancellationToken);

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

    private async Task<CloudWorkspaceDeletionReport> BuildDeletionReportAsync(Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken)
    {
        IReadOnlyList<CloudSubscription> subscriptions = await store.GetSubscriptionsAsync(null, workspaceId, cancellationToken);
        IReadOnlyList<CloudWorkspaceMember> members = await store.GetMembersAsync(workspaceId, cancellationToken);
        CloudBillingProfile? billing = await store.GetBillingProfileAsync(null, workspaceId, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int running = subscriptions.Count(subscription => subscription.DeletionPolicy != CloudSubscriptionDeletionPolicies.Always && subscription.IsRunningOn(now));
        int protectedCount = subscriptions.Count(subscription => subscription.DeletionPolicy == CloudSubscriptionDeletionPolicies.Never);
        int removable = subscriptions.Count(subscription => subscription.CanDeleteOn(now));

        CloudWorkspaceDeletionBlockers blockers = CloudWorkspaceDeletionBlockers.None;
        List<string> reasons = [];

        if (running > 0)
        {
            blockers |= CloudWorkspaceDeletionBlockers.ActiveSubscriptions;
            reasons.Add($"{running} subscription{(running == 1 ? " is" : "s are")} still running. Cancel {(running == 1 ? "it" : "them")} or wait for the term to end.");
        }

        if (protectedCount > 0)
        {
            blockers |= CloudWorkspaceDeletionBlockers.ProtectedSubscriptions;
            reasons.Add($"{protectedCount} subscription{(protectedCount == 1 ? "" : "s")} must be cleared by the application that created {(protectedCount == 1 ? "it" : "them")}.");
        }

        return new CloudWorkspaceDeletionReport
        {
            WorkspaceId = workspaceId,
            Blockers = blockers,
            ActiveSubscriptionCount = running,
            ProtectedSubscriptionCount = protectedCount,
            RemovableSubscriptionCount = removable,
            OtherMemberCount = members.Count(member => member.UserId != callerUserId),
            PaymentMethodCount = billing?.PaymentMethods.Count ?? 0,
            Reasons = reasons
        };
    }

    private async Task<bool> IsOwnerAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (workspace.OwnerUserId == callerUserId)
            return true;

        IReadOnlyList<CloudWorkspaceMember> members = await store.GetMembersAsync(workspace.Id, cancellationToken);
        CloudWorkspaceMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);

        return caller is { IsOwner: true } || (caller?.Roles.Contains("Owner", StringComparer.OrdinalIgnoreCase) ?? false);
    }

    private async Task<bool> CanManageAsync(CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (await IsOwnerAsync(workspace, callerUserId, cancellationToken))
            return true;

        IReadOnlyList<CloudWorkspaceMember> members = await store.GetMembersAsync(workspace.Id, cancellationToken);
        CloudWorkspaceMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);

        return caller?.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) ?? false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SubscriptionRegistry(
    ICloudLoginAccountStore store,
    ICloudLoginEventPublisher? eventPublisher = null) : ICloudLoginSubscriptionRegistry
{
    public async Task<bool> HasActiveAsync(string application, string reference, Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default)
        => (await GetActiveAsync(userId, workspaceId, cancellationToken)).Any(subscription => subscription.Application.Equals(application, StringComparison.OrdinalIgnoreCase) && subscription.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<CloudSubscription>> GetActiveAsync(Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default)
    {
        ValidateOwner(userId, workspaceId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return [.. (await store.GetSubscriptionsAsync(userId, workspaceId, cancellationToken)).Where(subscription => subscription.IsRunningOn(now))];
    }

    public async Task<IReadOnlyList<CloudSubscription>> GetForOwnerAsync(Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default)
    {
        ValidateOwner(userId, workspaceId);
        return await store.GetSubscriptionsAsync(userId, workspaceId, cancellationToken);
    }

    public async Task<CloudSubscription> SaveAsync(CloudSubscription subscription, CancellationToken cancellationToken = default)
    {
        ValidateOwner(subscription.UserId, subscription.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Application);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Reference);
        CloudSubscription? existing =
            await store.GetSubscriptionAsync(subscription.Id, cancellationToken);
        await store.SaveSubscriptionAsync(subscription, cancellationToken);
        if (eventPublisher != null)
        {
            string eventType = subscription.Status == CloudSubscriptionStatuses.Cancelled
                ? "Subscription.Cancelled"
                : existing == null
                    ? "Subscription.Created"
                    : "Subscription.Updated";
            string operation = eventType[(eventType.IndexOf('.') + 1)..];
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                eventType,
                "Subscription",
                subscription.Id,
                operation,
                new { subscription.Id, subscription.UserId, subscription.WorkspaceId }),
                cancellationToken);
        }
        return subscription;
    }

    public async Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        CloudSubscription subscription = await store.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Subscription '{subscriptionId}' was not found.");

        if (!subscription.CanDelete)
            throw new CloudSubscriptionDeletionBlockedException(subscription);

        await store.DeleteSubscriptionAsync(subscriptionId, cancellationToken);

        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Subscription.Deleted",
                "Subscription",
                subscription.Id,
                "Deleted",
                new { subscription.Id, subscription.UserId, subscription.WorkspaceId }),
                cancellationToken);
    }

    public Task<CloudSubscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => store.GetSubscriptionAsync(subscriptionId, cancellationToken);
    public Task<IReadOnlyList<CloudSubscription>> GetAllAsync(CancellationToken cancellationToken = default) => store.GetAllSubscriptionsAsync(cancellationToken);

    private static void ValidateOwner(Guid? userId, Guid? workspaceId)
    {
        if (userId is null && workspaceId is null)
            throw new ArgumentException("A user or workspace owner is required.");
    }
}
