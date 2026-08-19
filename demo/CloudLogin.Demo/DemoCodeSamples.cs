namespace CloudLogin.Demo;

public static class DemoCodeSamples
{
    public const string Registration = """
builder.Services.AddCloudLoginAccountRegistry();

// Replace the in-memory store with your private persistence adapter.
builder.Services.AddSingleton<ICloudLoginAccountStore, ApplicationAccountStore>();
""";

    public const string Organizations = """
CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", ownerUserId);
CloudLoginOrganizationMember member = await organizations.AddMemberAsync(
    organization.Id, userId, ["BillingAdmin", "Developer"]);
CloudLoginOrganizationInvitation invitation = await organizations.InviteAsync(
    organization.Id, "developer@example.com", ownerUserId,
    DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
""";

    public const string Subscriptions = """
await subscriptions.SaveAsync(new AccountSubscription
{
    OrganizationId = organizationId,
    Application = "cloud-business",
    Reference = "team-growth",
    AutoRenew = true,
    Provider = "MyFatoorah",
    ProviderReference = providerSubscriptionId,
    Metadata = applicationOwnedMetadata
});

bool active = await subscriptions.HasActiveAsync(
    "cloud-business", "team-growth", organizationId: organizationId);
""";

    public const string Billing = """
await accountStore.SaveBillingProfileAsync(new AccountBillingProfile
{
    OrganizationId = organizationId,
    ProviderCustomerReference = providerCustomerId,
    PaymentMethods =
    [
        new("MyFatoorah", providerPaymentMethodId, "KNET", IsDefault: true)
    ]
});

// CloudLogin stores references only. Execute charges through CloudPayments.
""";
}
