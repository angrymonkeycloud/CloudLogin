using System.Collections.Concurrent;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.Server.Services;

public sealed class InMemoryCloudLoginAccountStore : ICloudLoginAccountStore
{
    private readonly ConcurrentDictionary<Guid, CloudLoginOrganization> _organizations = new();
    private readonly ConcurrentDictionary<(Guid OrganizationId, Guid UserId), CloudLoginOrganizationMember> _members = new();
    private readonly ConcurrentDictionary<Guid, CloudLoginOrganizationInvitation> _invitations = new();
    private readonly ConcurrentDictionary<Guid, AccountSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<(Guid? UserId, Guid? OrganizationId), AccountBillingProfile> _billingProfiles = new();

    public Task<CloudLoginOrganization?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_organizations.TryGetValue(organizationId, out CloudLoginOrganization? organization) ? organization : null);
    public Task SaveOrganizationAsync(CloudLoginOrganization organization, CancellationToken cancellationToken = default) { _organizations[organization.Id] = organization; return Task.CompletedTask; }
    public Task<IReadOnlyList<CloudLoginOrganization>> GetOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IEnumerable<Guid> organizationIds = _members.Values.Where(member => member.UserId == userId).Select(member => member.OrganizationId).Distinct();
        return Task.FromResult<IReadOnlyList<CloudLoginOrganization>>([.. organizationIds.Select(id => _organizations.TryGetValue(id, out CloudLoginOrganization? organization) ? organization : null).OfType<CloudLoginOrganization>()]);
    }
    public Task<IReadOnlyList<CloudLoginOrganization>> GetAllOrganizationsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudLoginOrganization>>([.. _organizations.Values]);
    public Task<IReadOnlyList<CloudLoginOrganizationMember>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudLoginOrganizationMember>>([.. _members.Values.Where(member => member.OrganizationId == organizationId)]);
    public Task SaveMemberAsync(CloudLoginOrganizationMember member, CancellationToken cancellationToken = default) { _members[(member.OrganizationId, member.UserId)] = member; return Task.CompletedTask; }
    public Task SaveInvitationAsync(CloudLoginOrganizationInvitation invitation, CancellationToken cancellationToken = default) { _invitations[invitation.Id] = invitation; return Task.CompletedTask; }
    public Task<IReadOnlyList<CloudLoginOrganizationInvitation>> GetInvitationsAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudLoginOrganizationInvitation>>([.. _invitations.Values.Where(invitation => invitation.OrganizationId == organizationId)]);
    public Task<AccountSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(_subscriptions.TryGetValue(subscriptionId, out AccountSubscription? subscription) ? subscription : null);
    public Task<IReadOnlyList<AccountSubscription>> GetSubscriptionsAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountSubscription>>([.. _subscriptions.Values.Where(subscription => (userId is null || subscription.UserId == userId) && (organizationId is null || subscription.OrganizationId == organizationId))]);
    public Task<IReadOnlyList<AccountSubscription>> GetAllSubscriptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountSubscription>>([.. _subscriptions.Values]);
    public Task SaveSubscriptionAsync(AccountSubscription subscription, CancellationToken cancellationToken = default) { _subscriptions[subscription.Id] = subscription; return Task.CompletedTask; }
    public Task<AccountBillingProfile?> GetBillingProfileAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_billingProfiles.TryGetValue((userId, organizationId), out AccountBillingProfile? profile) ? profile : null);
    public Task SaveBillingProfileAsync(AccountBillingProfile profile, CancellationToken cancellationToken = default) { _billingProfiles[(profile.UserId, profile.OrganizationId)] = profile; return Task.CompletedTask; }

    public Task DeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) { _organizations.TryRemove(organizationId, out _); return Task.CompletedTask; }
    public Task DeleteMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) { _members.TryRemove((organizationId, userId), out _); return Task.CompletedTask; }
    public Task DeleteInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default) { _invitations.TryRemove(invitationId, out _); return Task.CompletedTask; }
    public Task DeleteSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) { _subscriptions.TryRemove(subscriptionId, out _); return Task.CompletedTask; }
    public Task DeleteBillingProfileAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default) { _billingProfiles.TryRemove((userId, organizationId), out _); return Task.CompletedTask; }
}

public sealed class OrganizationRegistry(
    ICloudLoginAccountStore store,
    ICloudLoginEventPublisher? eventPublisher = null,
    CloudLoginWebConfiguration? configuration = null) : IOrganizationRegistry
{
    private OrganizationConfiguration Options => configuration?.Organization ?? new OrganizationConfiguration();

    public async Task<CloudLoginOrganization> CreateAsync(string name, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        OrganizationQuota quota = await GetQuotaAsync(ownerUserId, cancellationToken);

        if (quota.RemainingOwned <= 0)
            throw new OrganizationLimitReachedException(OrganizationLimitKinds.Owned, quota.MaxOwned);

        if (quota.RemainingTotal <= 0)
            throw new OrganizationLimitReachedException(OrganizationLimitKinds.Membership, quota.MaxTotal);

        CloudLoginOrganization organization = new() { Name = name.Trim(), OwnerUserId = ownerUserId };
        CloudLoginOrganizationMember owner = new() { OrganizationId = organization.Id, UserId = ownerUserId, IsOwner = true, Roles = ["Owner"] };
        await store.SaveOrganizationAsync(organization, cancellationToken);
        await store.SaveMemberAsync(owner, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Organization.Created",
                "Organization",
                organization.Id,
                "Created",
                new { organization.Id, organization.OwnerUserId }),
                cancellationToken);
        return organization;
    }

    public Task<CloudLoginOrganization?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => store.GetOrganizationAsync(organizationId, cancellationToken);
    public Task<IReadOnlyList<CloudLoginOrganization>> GetOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => store.GetOrganizationsForUserAsync(userId, cancellationToken);
    public Task<IReadOnlyList<CloudLoginOrganization>> GetAllAsync(CancellationToken cancellationToken = default) => store.GetAllOrganizationsAsync(cancellationToken);
    public Task<IReadOnlyList<CloudLoginOrganizationMember>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default) => store.GetMembersAsync(organizationId, cancellationToken);

    public async Task<OrganizationQuota> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CloudLoginOrganization> organizations = await store.GetOrganizationsForUserAsync(userId, cancellationToken);
        OrganizationConfiguration options = Options;

        return new OrganizationQuota
        {
            Owned = organizations.Count(organization => organization.OwnerUserId == userId),
            MaxOwned = options.EffectiveMaxOwnedPerUser,
            Total = organizations.Count,
            MaxTotal = options.EffectiveMaxPerUser
        };
    }

    public async Task<CloudLoginOrganizationMember> AddMemberAsync(Guid organizationId, Guid userId, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default)
    {
        _ = await store.GetOrganizationAsync(organizationId, cancellationToken) ?? throw new KeyNotFoundException($"Organization '{organizationId}' was not found.");

        // Joining counts against the member's own membership cap. An existing member being
        // re-saved with new roles isn't a new membership, so it never trips the cap.
        IReadOnlyList<CloudLoginOrganizationMember> existingMembers = await store.GetMembersAsync(organizationId, cancellationToken);

        if (!existingMembers.Any(member => member.UserId == userId))
        {
            OrganizationQuota quota = await GetQuotaAsync(userId, cancellationToken);

            if (!quota.CanJoin)
                throw new OrganizationLimitReachedException(OrganizationLimitKinds.Membership, quota.MaxTotal);
        }

        CloudLoginOrganizationMember member = new() { OrganizationId = organizationId, UserId = userId, Roles = roles ?? [] };
        await store.SaveMemberAsync(member, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Organization.MembershipUpdated",
                "Organization",
                organizationId,
                "MembershipUpdated",
                new { member.OrganizationId, member.UserId, member.State }),
                cancellationToken);
        return member;
    }

    public async Task<CloudLoginOrganizationInvitation> InviteAsync(Guid organizationId, string recipient, Guid invitedByUserId, DateTimeOffset expiresOn, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        if (expiresOn <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresOn));
        _ = await store.GetOrganizationAsync(organizationId, cancellationToken) ?? throw new KeyNotFoundException($"Organization '{organizationId}' was not found.");
        CloudLoginOrganizationInvitation invitation = new() { OrganizationId = organizationId, Recipient = recipient.Trim(), InvitedByUserId = invitedByUserId, ExpiresOn = expiresOn, Roles = roles ?? [] };
        await store.SaveInvitationAsync(invitation, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Organization.InvitationCreated",
                "Organization",
                organizationId,
                "InvitationCreated",
                new { invitation.Id, invitation.OrganizationId }),
                cancellationToken);
        return invitation;
    }

    public async Task<CloudLoginOrganization> UpdateAsync(CloudLoginOrganization organization, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        CloudLoginOrganization existing = await store.GetOrganizationAsync(organization.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Organization '{organization.Id}' was not found.");

        if (!await CanManageAsync(existing, callerUserId, cancellationToken))
            throw new UnauthorizedAccessException("Only the organization's owner or an admin member may update its profile.");

        ArgumentException.ThrowIfNullOrWhiteSpace(organization.Name);
        existing.Name = organization.Name.Trim();
        existing.LegalName = Clean(organization.LegalName);
        existing.Website = Clean(organization.Website);
        existing.Phone = Clean(organization.Phone);
        existing.BillingEmail = Clean(organization.BillingEmail);
        existing.BillingContactName = Clean(organization.BillingContactName);
        existing.TaxId = Clean(organization.TaxId);
        existing.BillingAddress = new OrganizationAddress
        {
            Line1 = Clean(organization.BillingAddress?.Line1),
            Line2 = Clean(organization.BillingAddress?.Line2),
            City = Clean(organization.BillingAddress?.City),
            State = Clean(organization.BillingAddress?.State),
            PostalCode = Clean(organization.BillingAddress?.PostalCode),
            Country = Clean(organization.BillingAddress?.Country)
        };
        existing.UpdatedOn = DateTimeOffset.UtcNow;

        await store.SaveOrganizationAsync(existing, cancellationToken);
        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Organization.Updated",
                "Organization",
                existing.Id,
                "Updated",
                new { existing.Id, existing.OwnerUserId }),
                cancellationToken);
        return existing;
    }

    public async Task<OrganizationDeletionReport> GetDeletionReportAsync(Guid organizationId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        CloudLoginOrganization organization = await store.GetOrganizationAsync(organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Organization '{organizationId}' was not found.");

        if (!await IsOwnerAsync(organization, callerUserId, cancellationToken))
            throw new UnauthorizedAccessException("Only the organization's owner may delete it.");

        return await BuildDeletionReportAsync(organizationId, callerUserId, cancellationToken);
    }

    public async Task DeleteAsync(Guid organizationId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        OrganizationDeletionReport report = await GetDeletionReportAsync(organizationId, callerUserId, cancellationToken);

        if (!report.CanDelete)
            throw new OrganizationDeletionBlockedException(report);

        // Nothing here blocks any more, so clear the organization's own records before the
        // organization itself: a store that fails midway leaves an organization the owner can
        // retry, rather than orphaned members and subscriptions no one can reach.
        foreach (AccountSubscription subscription in await store.GetSubscriptionsAsync(null, organizationId, cancellationToken))
            await store.DeleteSubscriptionAsync(subscription.Id, cancellationToken);

        await store.DeleteBillingProfileAsync(null, organizationId, cancellationToken);

        foreach (CloudLoginOrganizationInvitation invitation in await store.GetInvitationsAsync(organizationId, cancellationToken))
            await store.DeleteInvitationAsync(invitation.Id, cancellationToken);

        foreach (CloudLoginOrganizationMember member in await store.GetMembersAsync(organizationId, cancellationToken))
            await store.DeleteMemberAsync(organizationId, member.UserId, cancellationToken);

        await store.DeleteOrganizationAsync(organizationId, cancellationToken);

        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Organization.Deleted",
                "Organization",
                organizationId,
                "Deleted",
                new { Id = organizationId, DeletedByUserId = callerUserId }),
                cancellationToken);
    }

    private async Task<OrganizationDeletionReport> BuildDeletionReportAsync(Guid organizationId, Guid callerUserId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountSubscription> subscriptions = await store.GetSubscriptionsAsync(null, organizationId, cancellationToken);
        IReadOnlyList<CloudLoginOrganizationMember> members = await store.GetMembersAsync(organizationId, cancellationToken);
        AccountBillingProfile? billing = await store.GetBillingProfileAsync(null, organizationId, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int running = subscriptions.Count(subscription => subscription.DeletionPolicy != SubscriptionDeletionPolicies.Always && subscription.IsRunningOn(now));
        int protectedCount = subscriptions.Count(subscription => subscription.DeletionPolicy == SubscriptionDeletionPolicies.Never);
        int removable = subscriptions.Count(subscription => subscription.CanDeleteOn(now));

        OrganizationDeletionBlockers blockers = OrganizationDeletionBlockers.None;
        List<string> reasons = [];

        if (running > 0)
        {
            blockers |= OrganizationDeletionBlockers.ActiveSubscriptions;
            reasons.Add($"{running} subscription{(running == 1 ? " is" : "s are")} still running. Cancel {(running == 1 ? "it" : "them")} or wait for the term to end.");
        }

        if (protectedCount > 0)
        {
            blockers |= OrganizationDeletionBlockers.ProtectedSubscriptions;
            reasons.Add($"{protectedCount} subscription{(protectedCount == 1 ? "" : "s")} must be cleared by the application that created {(protectedCount == 1 ? "it" : "them")}.");
        }

        return new OrganizationDeletionReport
        {
            OrganizationId = organizationId,
            Blockers = blockers,
            ActiveSubscriptionCount = running,
            ProtectedSubscriptionCount = protectedCount,
            RemovableSubscriptionCount = removable,
            OtherMemberCount = members.Count(member => member.UserId != callerUserId),
            PaymentMethodCount = billing?.PaymentMethods.Count ?? 0,
            Reasons = reasons
        };
    }

    private async Task<bool> IsOwnerAsync(CloudLoginOrganization organization, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (organization.OwnerUserId == callerUserId)
            return true;

        IReadOnlyList<CloudLoginOrganizationMember> members = await store.GetMembersAsync(organization.Id, cancellationToken);
        CloudLoginOrganizationMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);

        return caller is { IsOwner: true } || (caller?.Roles.Contains("Owner", StringComparer.OrdinalIgnoreCase) ?? false);
    }

    private async Task<bool> CanManageAsync(CloudLoginOrganization organization, Guid callerUserId, CancellationToken cancellationToken)
    {
        if (await IsOwnerAsync(organization, callerUserId, cancellationToken))
            return true;

        IReadOnlyList<CloudLoginOrganizationMember> members = await store.GetMembersAsync(organization.Id, cancellationToken);
        CloudLoginOrganizationMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);

        return caller?.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) ?? false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SubscriptionRegistry(
    ICloudLoginAccountStore store,
    ICloudLoginEventPublisher? eventPublisher = null) : ISubscriptionRegistry
{
    public async Task<bool> HasActiveAsync(string application, string reference, Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
        => (await GetActiveAsync(userId, organizationId, cancellationToken)).Any(subscription => subscription.Application.Equals(application, StringComparison.OrdinalIgnoreCase) && subscription.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<AccountSubscription>> GetActiveAsync(Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ValidateOwner(userId, organizationId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return [.. (await store.GetSubscriptionsAsync(userId, organizationId, cancellationToken)).Where(subscription => subscription.IsRunningOn(now))];
    }

    public async Task<IReadOnlyList<AccountSubscription>> GetForOwnerAsync(Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ValidateOwner(userId, organizationId);
        return await store.GetSubscriptionsAsync(userId, organizationId, cancellationToken);
    }

    public async Task<AccountSubscription> SaveAsync(AccountSubscription subscription, CancellationToken cancellationToken = default)
    {
        ValidateOwner(subscription.UserId, subscription.OrganizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Application);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Reference);
        AccountSubscription? existing =
            await store.GetSubscriptionAsync(subscription.Id, cancellationToken);
        await store.SaveSubscriptionAsync(subscription, cancellationToken);
        if (eventPublisher != null)
        {
            string eventType = subscription.Status == AccountSubscriptionStatuses.Cancelled
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
                new { subscription.Id, subscription.UserId, subscription.OrganizationId }),
                cancellationToken);
        }
        return subscription;
    }

    public async Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        AccountSubscription subscription = await store.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Subscription '{subscriptionId}' was not found.");

        if (!subscription.CanDelete)
            throw new SubscriptionDeletionBlockedException(subscription);

        await store.DeleteSubscriptionAsync(subscriptionId, cancellationToken);

        if (eventPublisher != null)
            await eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "Subscription.Deleted",
                "Subscription",
                subscription.Id,
                "Deleted",
                new { subscription.Id, subscription.UserId, subscription.OrganizationId }),
                cancellationToken);
    }

    public Task<AccountSubscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => store.GetSubscriptionAsync(subscriptionId, cancellationToken);
    public Task<IReadOnlyList<AccountSubscription>> GetAllAsync(CancellationToken cancellationToken = default) => store.GetAllSubscriptionsAsync(cancellationToken);

    private static void ValidateOwner(Guid? userId, Guid? organizationId)
    {
        if (userId is null && organizationId is null)
            throw new ArgumentException("A user or organization owner is required.");
    }
}
