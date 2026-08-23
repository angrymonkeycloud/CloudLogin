using System.Text.Json;

namespace AngryMonkey.CloudLogin.Models;

public class CloudSubscriptionModel
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string Application { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public CloudSubscriptionStatuses Status { get; set; } = CloudSubscriptionStatuses.Active;
    public DateTimeOffset? ExpiresOn { get; set; }
    public bool AutoRenew { get; set; }
    public string? Provider { get; set; }
    public string? ProviderReference { get; set; }
    public CloudSubscriptionDeletionPolicies DeletionPolicy { get; set; } = CloudSubscriptionDeletionPolicies.WhenExpired;
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

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

public static class CloudSubscriptionModelExtensions
{
    public static CloudSubscriptionModel ToModel(this CloudSubscription source) => new()
    {
        Id = source.Id,
        UserId = source.UserId,
        WorkspaceId = source.WorkspaceId,
        Application = source.Application,
        Reference = source.Reference,
        Status = source.Status,
        ExpiresOn = source.ExpiresOn,
        AutoRenew = source.AutoRenew,
        Provider = source.Provider,
        ProviderReference = source.ProviderReference,
        DeletionPolicy = source.DeletionPolicy,
        Metadata = new Dictionary<string, JsonElement>(source.Metadata)
    };
}
