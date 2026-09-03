namespace AngryMonkey.CloudLogin;

public sealed class CloudWorkspaceInvitation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid WorkspaceId { get; init; }
    public required string Recipient { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTimeOffset ExpiresOn { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public Guid InvitedByUserId { get; init; }

    public bool HasExpired => ExpiresOn <= DateTimeOffset.UtcNow;
}
