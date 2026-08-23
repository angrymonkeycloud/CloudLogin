namespace AngryMonkey.CloudLogin;

/// <summary>
/// Everything the account UI shows for one workspace in a single round trip: its profile,
/// the caller's standing in it, its members, its subscriptions, and its billing profile.
/// </summary>
public sealed record CloudWorkspaceDetail
{
    public required CloudWorkspace Workspace { get; init; }

    /// <summary>The caller owns this workspace.</summary>
    public bool IsOwner { get; init; }

    /// <summary>The caller may edit the workspace's profile and billing details.</summary>
    public bool CanManage { get; init; }

    /// <summary>The caller's roles within this workspace.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<CloudWorkspaceMemberProfile> Members { get; init; } = [];
    public IReadOnlyList<CloudSubscription> Subscriptions { get; init; } = [];
    public CloudBillingProfile? BillingProfile { get; init; }

    /// <summary>Deletion readiness. Populated only for callers who may delete the workspace.</summary>
    public CloudWorkspaceDeletionReport? Deletion { get; init; }
}
