namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Enables subscription management in the CloudLogin account experience.
/// </summary>
public sealed class SubscriptionConfiguration
{
    /// <summary>
    /// Whether account holders may remove subscription entries themselves. Removal still honours
    /// each entry's <see cref="AccountSubscription.DeletionPolicy"/>; turning this off refuses
    /// every self-service removal regardless of policy, leaving the owning application in charge.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool AllowSelfServiceDeletion { get; set; } = true;
}

/// <summary>
/// Enables organization management in the CloudLogin account experience, and caps how many
/// organizations one user may accumulate.
/// </summary>
public sealed class OrganizationConfiguration
{
    /// <summary>
    /// How many organizations a single user may create and own. Leave unset for
    /// <see cref="OrganizationLimits.DefaultMaxOwnedPerUser"/>; set
    /// <see cref="OrganizationLimits.Unlimited"/> to remove the cap, or 0 to stop users
    /// creating organizations at all (they can still be invited into one).
    /// </summary>
    public int? MaxOwnedPerUser { get; set; }

    /// <summary>
    /// How many organizations a single user may belong to in total, owned ones included.
    /// Leave unset for <see cref="OrganizationLimits.DefaultMaxPerUser"/>; set
    /// <see cref="OrganizationLimits.Unlimited"/> to remove the cap.
    /// </summary>
    public int? MaxPerUser { get; set; }

    /// <summary>
    /// The owned-organization cap actually enforced. A total cap below the owned cap wins,
    /// since every owned organization is also a membership.
    /// </summary>
    public int EffectiveMaxOwnedPerUser => Math.Min(
        Normalize(MaxOwnedPerUser, OrganizationLimits.DefaultMaxOwnedPerUser),
        EffectiveMaxPerUser);

    /// <summary>The membership cap actually enforced.</summary>
    public int EffectiveMaxPerUser => Normalize(MaxPerUser, OrganizationLimits.DefaultMaxPerUser);

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
