using System.Text.Json;

namespace AngryMonkey.CloudLogin.Models;

public class CloudBillingProfileModel
{
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string? ProviderCustomerReference { get; set; }
    public List<CloudPaymentMethodReferenceModel> PaymentMethods { get; set; } = [];
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
}

public static class CloudBillingProfileModelExtensions
{
    public static CloudBillingProfileModel ToModel(this CloudBillingProfile source) => new()
    {
        UserId = source.UserId,
        WorkspaceId = source.WorkspaceId,
        ProviderCustomerReference = source.ProviderCustomerReference,
        PaymentMethods = [.. source.PaymentMethods.Select(method => method.ToModel())],
        Metadata = new Dictionary<string, JsonElement>(source.Metadata)
    };
}
