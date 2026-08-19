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
    /// Updates organization profile fields (Name, BillingEmail, BillingContactName).
    /// Throws <see cref="UnauthorizedAccessException"/> if <paramref name="callerUserId"/> is not the organization's owner.
    /// </summary>
    Task<CloudLoginOrganization> UpdateAsync(CloudLoginOrganization organization, Guid callerUserId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionRegistry
{
    Task<bool> HasActiveAsync(string application, string reference, Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountSubscription>> GetActiveAsync(Guid? userId = null, Guid? organizationId = null, CancellationToken cancellationToken = default);
    Task<AccountSubscription> SaveAsync(AccountSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Looks up a subscription by id, regardless of owner. Used by the service-to-service lookup endpoint.</summary>
    Task<AccountSubscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Returns every subscription in the registry, regardless of owner or status. Used by the service-to-service lookup endpoint.</summary>
    Task<IReadOnlyList<AccountSubscription>> GetAllAsync(CancellationToken cancellationToken = default);
}
