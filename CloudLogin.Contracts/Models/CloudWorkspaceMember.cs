namespace AngryMonkey.CloudLogin;

public sealed class CloudWorkspaceMember
{
    public required Guid WorkspaceId { get; init; }
    public required Guid UserId { get; init; }
    public CloudWorkspaceMembershipStates State { get; init; } = CloudWorkspaceMembershipStates.Active;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public bool IsOwner { get; init; }
    public DateTimeOffset JoinedOn { get; init; } = DateTimeOffset.UtcNow;
}
