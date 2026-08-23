namespace AngryMonkey.CloudLogin;

public sealed record CloudLoginRemovePaymentMethodRequest(string Provider, string Reference, Guid? WorkspaceId = null);
