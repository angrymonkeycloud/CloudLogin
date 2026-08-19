namespace AngryMonkey.CloudLogin;

public sealed record CreateOrganizationRequest(string Name);

public sealed record InviteToOrganizationRequest(string Recipient, IReadOnlyList<string>? Roles = null);

public sealed record AddPaymentMethodRequest(AccountPaymentMethodReference Method, Guid? OrganizationId = null);
