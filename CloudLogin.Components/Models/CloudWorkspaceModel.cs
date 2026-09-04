using System.Text.Json;

namespace AngryMonkey.CloudLogin.Models;

public class CloudWorkspaceModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? UpdatedOn { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    public string? LegalName { get; set; }
    public string? AdditionalBillingInformation { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingPhone { get; set; }
    public string? Website { get; set; }
    public string? TaxNumber { get; set; }
    public CloudWorkspaceAddressModel BillingAddress { get; set; } = new();
}

public static class CloudWorkspaceModelExtensions
{
    public static CloudWorkspaceModel ToModel(this CloudWorkspace source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        OwnerUserId = source.OwnerUserId,
        CreatedOn = source.CreatedOn,
        UpdatedOn = source.UpdatedOn,
        Metadata = new Dictionary<string, JsonElement>(source.Metadata),
        LegalName = source.LegalName,
        AdditionalBillingInformation = source.AdditionalBillingInformation,
        BillingEmail = source.BillingEmail,
        BillingPhone = source.BillingPhone,
        Website = source.Website,
        TaxNumber = source.TaxNumber,
        BillingAddress = source.BillingAddress.ToModel()
    };

    public static CloudWorkspace ToContract(this CloudWorkspaceModel model, CloudWorkspace original) => new()
    {
        Id = original.Id,
        Name = model.Name,
        OwnerUserId = original.OwnerUserId,
        CreatedOn = original.CreatedOn,
        UpdatedOn = model.UpdatedOn,
        Metadata = new Dictionary<string, JsonElement>(model.Metadata),
        LegalName = model.LegalName,
        AdditionalBillingInformation = model.AdditionalBillingInformation,
        BillingEmail = model.BillingEmail,
        BillingPhone = model.BillingPhone,
        Website = model.Website,
        TaxNumber = model.TaxNumber,
        BillingAddress = model.BillingAddress.ToContract()
    };
}
