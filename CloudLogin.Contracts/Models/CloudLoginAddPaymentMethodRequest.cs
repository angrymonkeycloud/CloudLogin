namespace AngryMonkey.CloudLogin;

public sealed record CloudLoginAddPaymentMethodRequest(CloudPaymentMethodReference Method, Guid? WorkspaceId = null);
