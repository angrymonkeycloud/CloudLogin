namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLoginSubscriptionRegistry
{
    Task<bool> HasActiveAsync(string application, string reference, Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudSubscription>> GetActiveAsync(Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default);
    Task<CloudSubscription> SaveAsync(CloudSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Every subscription held by an owner, whatever its status. The account UI lists these so expired entries stay visible.</summary>
    Task<IReadOnlyList<CloudSubscription>> GetForOwnerAsync(Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a subscription from the registry, honouring its
    /// <see cref="CloudSubscription.DeletionPolicy"/>. Throws
    /// <see cref="CloudSubscriptionDeletionBlockedException"/> when the policy forbids it.
    /// </summary>
    Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Looks up a subscription by id, regardless of owner. Used by the service-to-service lookup endpoint.</summary>
    Task<CloudSubscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Returns every subscription in the registry, regardless of owner or status. Used by the service-to-service lookup endpoint.</summary>
    Task<IReadOnlyList<CloudSubscription>> GetAllAsync(CancellationToken cancellationToken = default);
}
