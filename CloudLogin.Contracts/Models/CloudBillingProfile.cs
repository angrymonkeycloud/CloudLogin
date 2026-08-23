using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public sealed class CloudBillingProfile
{
    public Guid? UserId { get; init; }
    public Guid? WorkspaceId { get; init; }
    public string? ProviderCustomerReference { get; init; }
    public IReadOnlyList<CloudPaymentMethodReference> PaymentMethods { get; init; } = [];
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];
}
