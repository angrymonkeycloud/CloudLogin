namespace AngryMonkey.CloudLogin;

/// <summary>
/// Everything the account UI shows for one workspace in a single round trip: its profile,
/// the caller's standing in it, and its members. Commercial records (subscriptions, orders,
/// payments) live in the owning applications, not here.
/// </summary>
public sealed record CloudWorkspaceDetail
{
    public required CloudWorkspace Workspace { get; init; }

    /// <summary>The caller owns this workspace.</summary>
    public bool IsOwner { get; init; }

    /// <summary>The caller may edit the workspace's profile and billing contact details.</summary>
    public bool CanManage { get; init; }

    /// <summary>The caller's roles within this workspace.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<CloudWorkspaceMemberProfile> Members { get; init; } = [];

    /// <summary>Deletion readiness. Populated only for callers who may delete the workspace.</summary>
    public CloudWorkspaceDeletionReport? Deletion { get; init; }
}
