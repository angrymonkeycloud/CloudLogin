using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public enum AccountSubscriptionStatuses
{
    Pending,
    Active,
    Suspended,
    Cancelled,
    Expired
}

/// <summary>
/// When a subscription may be removed from the registry. Removal deletes the registry entry;
/// it is not a cancellation, and the owning application remains responsible for ending
/// whatever the subscription paid for.
/// </summary>
public enum SubscriptionDeletionPolicies
{
    /// <summary>
    /// The default. Removable once the subscription has stopped running &mdash; it is past its
    /// expiry date, or its status is Cancelled or Expired. An organization holding one of these
    /// while it is still running can't be deleted.
    /// </summary>
    WhenExpired,

    /// <summary>Removable at any time, running or not.</summary>
    Always,

    /// <summary>
    /// Never removable through the account surface. Reserve for entries an application must keep
    /// for audit or accounting; they block organization deletion until the application clears them.
    /// </summary>
    Never
}

/// <summary>A lightweight registry entry. Applications own plan meaning, entitlement rules, usage, renewal policy, and provider workflows.</summary>
public sealed class AccountSubscription
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; }
    public Guid? OrganizationId { get; init; }
    public required string Application { get; init; }
    public required string Reference { get; init; }
    public AccountSubscriptionStatuses Status { get; init; } = AccountSubscriptionStatuses.Active;
    public DateTimeOffset? ExpiresOn { get; init; }
    public bool AutoRenew { get; init; }
    public string? Provider { get; init; }
    public string? ProviderReference { get; init; }

    /// <summary>
    /// Whether this entry may be removed from the registry, and when. Defaults to
    /// <see cref="SubscriptionDeletionPolicies.WhenExpired"/>.
    /// </summary>
    public SubscriptionDeletionPolicies DeletionPolicy { get; init; } = SubscriptionDeletionPolicies.WhenExpired;

    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    /// <summary>Still running: an Active status that hasn't passed its expiry date.</summary>
    public bool IsRunningOn(DateTimeOffset moment)
        => Status == AccountSubscriptionStatuses.Active && (ExpiresOn is null || ExpiresOn > moment);

    /// <summary>Still running as of now.</summary>
    public bool IsRunning => IsRunningOn(DateTimeOffset.UtcNow);

    /// <summary>Whether <see cref="DeletionPolicy"/> permits removing this entry at <paramref name="moment"/>.</summary>
    public bool CanDeleteOn(DateTimeOffset moment) => DeletionPolicy switch
    {
        SubscriptionDeletionPolicies.Always => true,
        SubscriptionDeletionPolicies.Never => false,
        _ => !IsRunningOn(moment)
    };

    /// <summary>Whether this entry can be removed right now.</summary>
    public bool CanDelete => CanDeleteOn(DateTimeOffset.UtcNow);
}

public sealed record AccountPaymentMethodReference(string Provider, string Reference, string? DisplayName = null, bool IsDefault = false);

public sealed class AccountBillingProfile
{
    public Guid? UserId { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? ProviderCustomerReference { get; init; }
    public IReadOnlyList<AccountPaymentMethodReference> PaymentMethods { get; init; } = [];
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];
}

/// <summary>Thrown when a subscription's <see cref="AccountSubscription.DeletionPolicy"/> forbids removing it.</summary>
public sealed class SubscriptionDeletionBlockedException(AccountSubscription subscription)
    : InvalidOperationException(subscription.DeletionPolicy == SubscriptionDeletionPolicies.Never
        ? $"'{subscription.Application}' subscriptions can't be removed from the account. Contact the application to clear this entry."
        : $"'{subscription.Application}' is still running. Cancel it or wait for it to expire before removing it.")
{
    public Guid SubscriptionId { get; } = subscription.Id;
    public SubscriptionDeletionPolicies DeletionPolicy { get; } = subscription.DeletionPolicy;
}
