namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Enables subscription management in the CloudLogin account experience.
/// </summary>
public sealed class SubscriptionConfiguration
{
    /// <summary>
    /// Whether account holders may remove subscription entries themselves. Removal still honours
    /// each entry's <see cref="CloudSubscription.DeletionPolicy"/>; turning this off refuses
    /// every self-service removal regardless of policy, leaving the owning application in charge.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool AllowSelfServiceDeletion { get; set; } = true;
}

/// <summary>
/// Enables workspace management in the CloudLogin account experience, and caps how many
/// workspaces one user may accumulate.
/// </summary>
public sealed class WorkspaceConfiguration
{
    /// <summary>
    /// How many workspaces a single user may create and own. Leave unset for
    /// <see cref="CloudWorkspaceLimits.DefaultMaxOwnedPerUser"/>; set
    /// <see cref="CloudWorkspaceLimits.Unlimited"/> to remove the cap, or 0 to stop users
    /// creating workspaces at all (they can still be invited into one).
    /// </summary>
    public int? MaxOwnedPerUser { get; set; }

    /// <summary>
    /// How many workspaces a single user may belong to in total, owned ones included.
    /// Leave unset for <see cref="CloudWorkspaceLimits.DefaultMaxPerUser"/>; set
    /// <see cref="CloudWorkspaceLimits.Unlimited"/> to remove the cap.
    /// </summary>
    public int? MaxPerUser { get; set; }

    /// <summary>
    /// The owned-workspace cap actually enforced. A total cap below the owned cap wins,
    /// since every owned workspace is also a membership.
    /// </summary>
    public int EffectiveMaxOwnedPerUser => Math.Min(
        Normalize(MaxOwnedPerUser, CloudWorkspaceLimits.DefaultMaxOwnedPerUser),
        EffectiveMaxPerUser);

    /// <summary>The membership cap actually enforced.</summary>
    public int EffectiveMaxPerUser => Normalize(MaxPerUser, CloudWorkspaceLimits.DefaultMaxPerUser);

    /// <summary>
    /// Display name for one workspace, shown throughout the account UI and in messages
    /// surfaced to end users. "Workspace" is a deliberately generic default — set this to
    /// whatever the concept is called in your product, e.g. "Organization", "Team", or
    /// "Business". Does not affect API routes, JSON property names, or webhook event names,
    /// which stay stable regardless of how this is labeled.
    /// </summary>
    public string SingularLabel { get; set; } = "Workspace";

    /// <summary>
    /// Display name for many workspaces, e.g. "Organizations", "Teams", or "Businesses".
    /// Paired with <see cref="SingularLabel"/>; set both together.
    /// </summary>
    public string PluralLabel { get; set; } = "Workspaces";

    private static int Normalize(int? configured, int fallback)
        => configured is null ? fallback : Math.Max(0, configured.Value);
}

/// <summary>
/// Enables payment-method management in the CloudLogin account experience.
/// Reserved for payment-specific options as the feature evolves.
/// </summary>
public sealed class PaymentConfiguration
{
}
