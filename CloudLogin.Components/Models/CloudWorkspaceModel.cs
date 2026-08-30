using System.Text.Json;

namespace AngryMonkey.CloudLogin.Models;

public class CloudWorkspaceModel
{
    public Guid ID { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? UpdatedOn { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingContactName { get; set; }
    public string? TaxId { get; set; }
    public CloudWorkspaceAddressModel BillingAddress { get; set; } = new();
}

public static class CloudWorkspaceModelExtensions
{
    public static CloudWorkspaceModel ToModel(this CloudWorkspace source) => new()
    {
        ID = source.ID,
        Name = source.Name,
        OwnerUserId = source.OwnerUserId,
        CreatedOn = source.CreatedOn,
        UpdatedOn = source.UpdatedOn,
        Metadata = new Dictionary<string, JsonElement>(source.Metadata),
        LegalName = source.LegalName,
        Website = source.Website,
        Phone = source.Phone,
        BillingEmail = source.BillingEmail,
        BillingContactName = source.BillingContactName,
        TaxId = source.TaxId,
        BillingAddress = source.BillingAddress.ToModel()
    };

    public static CloudWorkspace ToContract(this CloudWorkspaceModel model, CloudWorkspace original) => new()
    {
        ID = original.ID,
        Name = model.Name,
        OwnerUserId = original.OwnerUserId,
        CreatedOn = original.CreatedOn,
        UpdatedOn = model.UpdatedOn,
        Metadata = new Dictionary<string, JsonElement>(model.Metadata),
        LegalName = model.LegalName,
        Website = model.Website,
        Phone = model.Phone,
        BillingEmail = model.BillingEmail,
        BillingContactName = model.BillingContactName,
        TaxId = model.TaxId,
        BillingAddress = model.BillingAddress.ToContract()
    };
}
