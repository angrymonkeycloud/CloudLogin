using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public enum OrganizationMembershipStates
{
    Invited,
    Active,
    Suspended,
    Removed
}

public sealed class CloudLoginOrganization
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid OwnerUserId { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    /// <summary>Billing contact email for this organization. Editable by the organization owner.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>Billing contact name for this organization. Editable by the organization owner.</summary>
    public string? BillingContactName { get; set; }
}

public sealed class CloudLoginOrganizationMember
{
    public required Guid OrganizationId { get; init; }
    public required Guid UserId { get; init; }
    public OrganizationMembershipStates State { get; init; } = OrganizationMembershipStates.Active;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public bool IsOwner { get; init; }
    public DateTimeOffset JoinedOn { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CloudLoginOrganizationInvitation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid OrganizationId { get; init; }
    public required string Recipient { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTimeOffset ExpiresOn { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public Guid InvitedByUserId { get; init; }
}
