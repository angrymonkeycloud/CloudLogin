namespace AngryMonkey.CloudLogin;

public sealed record CloudPaymentMethodReference(string Provider, string Reference, string? DisplayName = null, bool IsDefault = false);
