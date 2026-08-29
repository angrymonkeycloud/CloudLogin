namespace AngryMonkey.CloudLogin;

/// <summary>
/// What the owner should know before deleting a workspace. CloudLogin holds no commercial
/// records any more, so nothing here blocks deletion; <see cref="OtherMemberCount"/> is
/// surfaced so the owner can see who else loses access before confirming.
/// </summary>
public sealed record CloudWorkspaceDeletionReport
{
    public required Guid WorkspaceId { get; init; }
    public CloudWorkspaceDeletionBlockers Blockers { get; init; }
    public int OtherMemberCount { get; init; }

    public bool CanDelete => Blockers == CloudWorkspaceDeletionBlockers.None;

    /// <summary>Human-readable blockers, ready to render in a confirmation dialog.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];
}
