namespace AngryMonkey.CloudLogin;

/// <summary>
/// What stands between a workspace and deletion. <see cref="OtherMemberCount"/> never blocks
/// deletion; it's surfaced so the owner can see who else loses access before confirming.
/// </summary>
public sealed record CloudWorkspaceDeletionReport
{
    public required Guid WorkspaceId { get; init; }
    public CloudWorkspaceDeletionBlockers Blockers { get; init; }
    public int ActiveSubscriptionCount { get; init; }
    public int ProtectedSubscriptionCount { get; init; }
    public int RemovableSubscriptionCount { get; init; }
    public int OtherMemberCount { get; init; }
    public int PaymentMethodCount { get; init; }

    public bool CanDelete => Blockers == CloudWorkspaceDeletionBlockers.None;

    /// <summary>Human-readable blockers, ready to render in a confirmation dialog.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];
}
