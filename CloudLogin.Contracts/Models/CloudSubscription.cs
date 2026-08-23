using System.Text.Json;

namespace AngryMonkey.CloudLogin;

/// <summary>A lightweight registry entry. Applications own plan meaning, entitlement rules, usage, renewal policy, and provider workflows.</summary>
public sealed class CloudSubscription
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; }
    public Guid? WorkspaceId { get; init; }
    public required string Application { get; init; }
    public required string Reference { get; init; }
    public CloudSubscriptionStatuses Status { get; init; } = CloudSubscriptionStatuses.Active;
    public DateTimeOffset? ExpiresOn { get; init; }
    public bool AutoRenew { get; init; }
    public string? Provider { get; init; }
    public string? ProviderReference { get; init; }

    /// <summary>
    /// Whether this entry may be removed from the registry, and when. Defaults to
    /// <see cref="CloudSubscriptionDeletionPolicies.WhenExpired"/>.
    /// </summary>
    public CloudSubscriptionDeletionPolicies DeletionPolicy { get; init; } = CloudSubscriptionDeletionPolicies.WhenExpired;

    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    /// <summary>Still running: an Active status that hasn't passed its expiry date.</summary>
    public bool IsRunningOn(DateTimeOffset moment)
        => Status == CloudSubscriptionStatuses.Active && (ExpiresOn is null || ExpiresOn > moment);

    /// <summary>Still running as of now.</summary>
    public bool IsRunning => IsRunningOn(DateTimeOffset.UtcNow);

    /// <summary>Whether <see cref="DeletionPolicy"/> permits removing this entry at <paramref name="moment"/>.</summary>
    public bool CanDeleteOn(DateTimeOffset moment) => DeletionPolicy switch
    {
        CloudSubscriptionDeletionPolicies.Always => true,
        CloudSubscriptionDeletionPolicies.Never => false,
        _ => !IsRunningOn(moment)
    };

    /// <summary>Whether this entry can be removed right now.</summary>
    public bool CanDelete => CanDeleteOn(DateTimeOffset.UtcNow);
}
