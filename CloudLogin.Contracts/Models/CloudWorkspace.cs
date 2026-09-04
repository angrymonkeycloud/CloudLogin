using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public sealed class CloudWorkspace
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid OwnerUserId { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedOn { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    /// <summary>Registered legal entity name, when it differs from the display name.</summary>
    public string? LegalName { get; set; }

    /// <summary>
    /// Free text printed under the legal name on anything addressed to this workspace: a billing
    /// contact, a department, a PO reference. Editable by the workspace owner.
    /// </summary>
    public string? AdditionalBillingInformation { get; set; }

    /// <summary>Billing email for this workspace. Editable by the workspace owner.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>Billing phone number for this workspace. Editable by the workspace owner.</summary>
    public string? BillingPhone { get; set; }

    /// <summary>Public website for this workspace.</summary>
    public string? Website { get; set; }

    /// <summary>Tax or VAT registration number printed on this workspace's invoices.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>Postal address invoices are issued to.</summary>
    public CloudWorkspaceAddress BillingAddress { get; set; } = new();
}
