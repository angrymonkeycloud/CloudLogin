namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>Workspace lifecycle states.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum WorkspaceStates
{
    Active,
    Suspended,
    PendingDeletion,
    Deleted
}

/// <summary>
/// A workspace profile in the <c>Workspaces</c> container (partition key <c>/id</c>).
/// <para>
/// Holds profile, lifecycle, and timestamps only. Ownership is not a field here: members, roles,
/// owners, and invitations live in the <c>WorkspaceAccess</c> container so a workspace can have
/// any number of owners and membership changes never contend on the profile document.
/// </para>
/// </summary>
public sealed class WorkspaceDocument : CloudLoginCoreDocument
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? TaxId { get; set; }

    public string? BillingContactName { get; set; }
    public string? BillingContactEmail { get; set; }
    public string? BillingContactPhone { get; set; }

    public WorkspaceStates State { get; set; } = WorkspaceStates.Active;

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }
}
