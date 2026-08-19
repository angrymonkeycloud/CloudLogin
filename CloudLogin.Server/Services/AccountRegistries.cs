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
    public Task<AccountSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(_subscriptions.TryGetValue(subscriptionId, out AccountSubscription? subscription) ? subscription : null);
    public Task<IReadOnlyList<AccountSubscription>> GetSubscriptionsAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountSubscription>>([.. _subscriptions.Values.Where(subscription => (userId is null || subscription.UserId == userId) && (organizationId is null || subscription.OrganizationId == organizationId))]);
    public Task<IReadOnlyList<AccountSubscription>> GetAllSubscriptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AccountSubscription>>([.. _subscriptions.Values]);
    public Task SaveSubscriptionAsync(AccountSubscription subscription, CancellationToken cancellationToken = default) { _subscriptions[subscription.Id] = subscription; return Task.CompletedTask; }
    public Task<AccountBillingProfile?> GetBillingProfileAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_billingProfiles.TryGetValue((userId, organizationId), out AccountBillingProfile? profile) ? profile : null);
    public Task SaveBillingProfileAsync(AccountBillingProfile profile, CancellationToken cancellationToken = default) { _billingProfiles[(profile.UserId, profile.OrganizationId)] = profile; return Task.CompletedTask; }
}

public sealed class OrganizationRegistry(
    ICloudLoginAccountStore store,
    ICloudLoginEventPublisher? eventPublisher = null) : IOrganizationRegistry
{
    public async Task<CloudLoginOrganization> CreateAsync(string name, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
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

    public async Task<CloudLoginOrganizationMember> AddMemberAsync(Guid organizationId, Guid userId, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default)
    {
        _ = await store.GetOrganizationAsync(organizationId, cancellationToken) ?? throw new KeyNotFoundException($"Organization '{organizationId}' was not found.");
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

        bool isOwner = existing.OwnerUserId == callerUserId;
        if (!isOwner)
        {
            IReadOnlyList<CloudLoginOrganizationMember> members = await store.GetMembersAsync(organization.Id, cancellationToken);
            CloudLoginOrganizationMember? caller = members.FirstOrDefault(member => member.UserId == callerUserId);
            isOwner = caller is { IsOwner: true } || (caller?.Roles.Contains("Owner", StringComparer.OrdinalIgnoreCase) ?? false) || (caller?.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) ?? false);
        }

        if (!isOwner)
            throw new UnauthorizedAccessException("Only the organization's owner or an admin member may update its profile.");

        ArgumentException.ThrowIfNullOrWhiteSpace(organization.Name);
        existing.Name = organization.Name.Trim();
        existing.BillingEmail = string.IsNullOrWhiteSpace(organization.BillingEmail) ? null : organization.BillingEmail.Trim();
        existing.BillingContactName = string.IsNullOrWhiteSpace(organization.BillingContactName) ? null : organization.BillingContactName.Trim();

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
        return [.. (await store.GetSubscriptionsAsync(userId, organizationId, cancellationToken)).Where(subscription => subscription.Status == AccountSubscriptionStatuses.Active && (subscription.ExpiresOn is null || subscription.ExpiresOn > now))];
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

    public Task<AccountSubscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => store.GetSubscriptionAsync(subscriptionId, cancellationToken);
    public Task<IReadOnlyList<AccountSubscription>> GetAllAsync(CancellationToken cancellationToken = default) => store.GetAllSubscriptionsAsync(cancellationToken);

    private static void ValidateOwner(Guid? userId, Guid? organizationId)
    {
        if (userId is null && organizationId is null)
            throw new ArgumentException("A user or organization owner is required.");
    }
}
