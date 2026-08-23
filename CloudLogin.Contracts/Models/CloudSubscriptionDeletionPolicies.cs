namespace AngryMonkey.CloudLogin;

/// <summary>
/// When a subscription may be removed from the registry. Removal deletes the registry entry;
/// it is not a cancellation, and the owning application remains responsible for ending
/// whatever the subscription paid for.
/// </summary>
public enum CloudSubscriptionDeletionPolicies
{
    /// <summary>
    /// The default. Removable once the subscription has stopped running &mdash; it is past its
    /// expiry date, or its status is Cancelled or Expired. A workspace holding one of these
    /// while it is still running can't be deleted.
    /// </summary>
    WhenExpired,

    /// <summary>Removable at any time, running or not.</summary>
    Always,

    /// <summary>
    /// Never removable through the account surface. Reserve for entries an application must keep
    /// for audit or accounting; they block workspace deletion until the application clears them.
    /// </summary>
    Never
}
