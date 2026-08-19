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
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];
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
