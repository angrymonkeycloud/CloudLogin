using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>Kinds of records in the <c>WorkspaceAccess</c> container.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum WorkspaceAccessKinds
{
    Membership,
    Invitation
}

/// <summary>Membership/invitation states.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum WorkspaceAccessStates
{
    Active,
    Disabled,
    Pending,
    Declined,
    Revoked
}

/// <summary>Workspace roles, evaluated by policy — see <c>WorkspaceRolePolicy</c>.</summary>
public static class WorkspaceRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
}

/// <summary>
/// A membership or invitation in the <c>WorkspaceAccess</c> container (partition key
/// <c>/workspaceId</c>).
/// <para>
/// Memberships are permanent: they carry no <c>ttl</c> and no expiry. Invitations are expiring:
/// they always carry a positive <c>ttl</c> recomputed from <see cref="ExpiresOn"/> on every
/// write, so Cosmos removes them natively. Multiple members can hold the Owner role; the
/// application layer enforces that at least one active owner always remains.
/// </para>
/// </summary>
public sealed class WorkspaceAccessDocument : CloudLoginCoreDocument, IExpiringDocument
{
    /// <summary>Partition key.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    public WorkspaceAccessKinds Kind { get; set; }
    public WorkspaceAccessStates State { get; set; } = WorkspaceAccessStates.Active;

    /// <summary>The member's user id. Null on invitations that have not been accepted.</summary>
    public string? UserId { get; set; }

    /// <summary>Roles held by the member, or granted on invitation acceptance.</summary>
    public List<string> Roles { get; set; } = [];

    // ── Invitation fields (Kind = Invitation) ─────────────────────────────────

    /// <summary>The invited email address or phone number, normalized.</summary>
    public string? RecipientKey { get; set; }

    /// <summary>The invited recipient exactly as entered, for display in the invitation.</summary>
    public string? RecipientDisplay { get; set; }

    /// <summary>The user who created the invitation.</summary>
    public string? InvitedByUserId { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }

    // ── Expiry (invitations only; memberships never carry ttl) ────────────────

    public DateTimeOffset? ExpiresOn { get; set; }

    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    // ── Document id helpers ───────────────────────────────────────────────────

    public static string MembershipId(Guid userId) => $"member|{userId}";
    public static string InvitationId(Guid invitationId) => $"invite|{invitationId}";
}
