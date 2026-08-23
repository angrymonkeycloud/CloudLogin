namespace AngryMonkey.CloudLogin;

/// <summary>Thrown when a subscription's <see cref="CloudSubscription.DeletionPolicy"/> forbids removing it.</summary>
public sealed class CloudSubscriptionDeletionBlockedException(CloudSubscription subscription)
    : InvalidOperationException(subscription.DeletionPolicy == CloudSubscriptionDeletionPolicies.Never
        ? $"'{subscription.Application}' subscriptions can't be removed from the account. Contact the application to clear this entry."
        : $"'{subscription.Application}' is still running. Cancel it or wait for it to expire before removing it.")
{
    public Guid SubscriptionId { get; } = subscription.Id;
    public CloudSubscriptionDeletionPolicies DeletionPolicy { get; } = subscription.DeletionPolicy;
}
