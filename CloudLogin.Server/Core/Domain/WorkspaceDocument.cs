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

    /// <summary>
    /// Free text printed under the legal name on anything addressed to this workspace: a billing
    /// contact, a department, a PO reference. It replaces the single billing-contact-name field,
    /// which could hold only one of those.
    /// </summary>
    public string? AdditionalBillingInformation { get; set; }

    public string? BillingEmail { get; set; }
    public string? BillingPhone { get; set; }
    public string? Website { get; set; }
    public string? TaxNumber { get; set; }

    /// <summary>Postal address invoices are issued to.</summary>
    public WorkspaceAddress BillingAddress { get; set; } = new();

    public WorkspaceStates State { get; set; } = WorkspaceStates.Active;

    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }
}

/// <summary>
/// The stored form of a workspace's billing address. Mirrors the transport's
/// <c>CloudWorkspaceAddress</c>, the way <c>UserContact</c> mirrors <c>CloudLoginInput</c>: the
/// document shape is the core's own and does not move when a contract does.
/// </summary>
public sealed class WorkspaceAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Line1)
        && string.IsNullOrWhiteSpace(Line2)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(State)
        && string.IsNullOrWhiteSpace(PostalCode)
        && string.IsNullOrWhiteSpace(Country);
}
