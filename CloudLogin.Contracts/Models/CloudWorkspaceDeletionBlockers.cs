namespace AngryMonkey.CloudLogin;

/// <summary>Reasons a workspace can't be deleted yet.</summary>
[Flags]
public enum CloudWorkspaceDeletionBlockers
{
    None = 0,

    /// <summary>Subscriptions that are still running. They must expire or be cancelled first.</summary>
    ActiveSubscriptions = 1,

    /// <summary>Subscriptions the owning application marked as never deletable.</summary>
    ProtectedSubscriptions = 2
}
