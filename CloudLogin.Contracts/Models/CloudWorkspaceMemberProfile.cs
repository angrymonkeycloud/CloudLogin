namespace AngryMonkey.CloudLogin;

/// <summary>
/// A member of a workspace as the members list renders them: the membership record plus
/// just enough of the user's profile to name and picture the row. Nothing else about the user
/// crosses the workspace boundary.
/// </summary>
public sealed record CloudWorkspaceMemberProfile
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? EmailAddress { get; init; }
    public string? ProfilePicture { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public bool IsOwner { get; init; }
    public CloudWorkspaceMembershipStates State { get; init; } = CloudWorkspaceMembershipStates.Active;
    public DateTimeOffset JoinedOn { get; init; }

    /// <summary>The best available label for this member, falling back to a shortened id.</summary>
    public string Label => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName!
        : !string.IsNullOrWhiteSpace(EmailAddress)
            ? EmailAddress!
            : $"Member {UserId.ToString()[..8]}";
}
