using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public sealed class CloudWorkspace
{
    public Guid ID { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid OwnerUserId { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedOn { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    /// <summary>Registered legal entity name, when it differs from the display name.</summary>
    public string? LegalName { get; set; }

    /// <summary>Public website for this workspace.</summary>
    public string? Website { get; set; }

    /// <summary>Contact phone number for this workspace.</summary>
    public string? Phone { get; set; }

    /// <summary>Billing contact email for this workspace. Editable by the workspace owner.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>Billing contact name for this workspace. Editable by the workspace owner.</summary>
    public string? BillingContactName { get; set; }

    /// <summary>Tax or VAT registration number printed on this workspace's invoices.</summary>
    public string? TaxId { get; set; }

    /// <summary>Postal address invoices are issued to.</summary>
    public CloudWorkspaceAddress BillingAddress { get; set; } = new();
}
