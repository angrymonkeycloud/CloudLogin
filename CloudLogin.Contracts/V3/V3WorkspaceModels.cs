namespace AngryMonkey.CloudLogin.V3;

public sealed record V3WorkspaceResponse
{
    public required Guid WorkspaceId { get; init; }
    public required string Name { get; init; }
    public string? Website { get; init; }
    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>The caller's roles in this workspace.</summary>
    public List<string> MyRoles { get; init; } = [];
}

public sealed record V3WorkspaceMemberResponse
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public List<string> Roles { get; init; } = [];

    /// <summary>"Active" or "Disabled".</summary>
    public required string State { get; init; }
}

public sealed record V3WorkspaceInvitationResponse
{
    public required string InvitationId { get; init; }
    public required string Recipient { get; init; }
    public List<string> Roles { get; init; } = [];
    public DateTimeOffset ExpiresOn { get; init; }
}

public sealed record V3CreateWorkspaceRequest
{
    public required string Name { get; init; }
}

public sealed record V3UpdateWorkspaceRequest
{
    public string? Name { get; init; }
    public string? Website { get; init; }
}

public sealed record V3InviteMemberRequest
{
    /// <summary>Email address or phone number of the invitee.</summary>
    public required string Recipient { get; init; }

    public List<string> Roles { get; init; } = [];
}

public sealed record V3UpdateMemberRolesRequest
{
    public required List<string> Roles { get; init; }
}

public sealed record V3SetMemberStateRequest
{
    /// <summary>Active or Disabled.</summary>
    public required string State { get; init; }
}
