namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLoginAccountStore
{
    Task<CloudLoginOrganization?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task SaveOrganizationAsync(CloudLoginOrganization organization, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudLoginOrganization>> GetOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudLoginOrganization>> GetAllOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudLoginOrganizationMember>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task SaveMemberAsync(CloudLoginOrganizationMember member, CancellationToken cancellationToken = default);
    Task SaveInvitationAsync(CloudLoginOrganizationInvitation invitation, CancellationToken cancellationToken = default);
    Task<AccountSubscription?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountSubscription>> GetSubscriptionsAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountSubscription>> GetAllSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task SaveSubscriptionAsync(AccountSubscription subscription, CancellationToken cancellationToken = default);
    Task<AccountBillingProfile?> GetBillingProfileAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default);
    Task SaveBillingProfileAsync(AccountBillingProfile profile, CancellationToken cancellationToken = default);

    // ── Removal ───────────────────────────────────────────────────────────────
    // Stores written before organization deletion existed keep compiling: each member below
    // reports that the store can't remove records rather than silently leaving them behind.
    // Implement all of them to let owners delete their organizations.

    /// <summary>Invitations issued for an organization, expired ones included.</summary>
    Task<IReadOnlyList<CloudLoginOrganizationInvitation>> GetInvitationsAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement invitation reads.");

    Task DeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");

    Task DeleteMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");

    Task DeleteInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");

    Task DeleteSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");

    Task DeleteBillingProfileAsync(Guid? userId, Guid? organizationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement deletion.");
}

public interface IOrganizationRegistry
{
    Task<CloudLoginOrganization> CreateAsync(string name, Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<CloudLoginOrganization?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudLoginOrganization>> GetOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudLoginOrganization>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudLoginOrganizationMember>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<CloudLoginOrganizationMember> AddMemberAsync(Guid organizationId, Guid userId, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default);
    Task<CloudLoginOrganizationInvitation> InviteAsync(Guid organizationId, string recipient, Guid invitedByUserId, DateTimeOffset expiresAt, IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates organization profile fields (Name, contact details, and billing information).
    /// Throws <see cref="UnauthorizedAccessException"/> if <paramref name="callerUserId"/> is not the organization's owner.
    /// </summary>
    Task<CloudLoginOrganization> UpdateAsync(CloudLoginOrganization organization, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>How many organizations the user owns and belongs to, against the configured caps.</summary>
    Task<OrganizationQuota> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What currently prevents deleting an organization, and what deletion would take with it.
    /// Throws <see cref="UnauthorizedAccessException"/> unless the caller owns the organization.
    /// </summary>
    Task<OrganizationDeletionReport> GetDeletionReportAsync(Guid organizationId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an organization along with its memberships, invitations, billing profile, and
    /// removable subscriptions. Throws <see cref="UnauthorizedAccessException"/> unless the caller
    /// owns it, and <see cref="OrganizationDeletionBlockedException"/> while any subscription
    /// still blocks deletion.
    /// </summary>
    Task DeleteAsync(Guid organizationId, Guid callerUserId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionRegistry
{
    Task<bool> HasActiveAsync(string application, string reference, Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountSubscription>> GetActiveAsync(Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default);
    Task<AccountSubscription> SaveAsync(AccountSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Every subscription held by an owner, whatever its status. The account UI lists these so expired entries stay visible.</summary>
    Task<IReadOnlyList<AccountSubscription>> GetForOwnerAsync(Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a subscription from the registry, honouring its
    /// <see cref="AccountSubscription.DeletionPolicy"/>. Throws
    /// <see cref="SubscriptionDeletionBlockedException"/> when the policy forbids it.
    /// </summary>
    Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Looks up a subscription by id, regardless of owner. Used by the service-to-service lookup endpoint.</summary>
    Task<AccountSubscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Returns every subscription in the registry, regardless of owner or status. Used by the service-to-service lookup endpoint.</summary>
    Task<IReadOnlyList<AccountSubscription>> GetAllAsync(CancellationToken cancellationToken = default);
}
