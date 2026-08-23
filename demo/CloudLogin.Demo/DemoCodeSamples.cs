namespace CloudLogin.Demo;

public static class DemoCodeSamples
{
    public const string Registration = """
builder.Services.AddCloudLoginAccountRegistry();

// Replace the in-memory store with your private persistence adapter.
builder.Services.AddSingleton<ICloudLoginAccountStore, ApplicationAccountStore>();
""";

    public const string Workspaces = """
CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerUserId);
CloudWorkspaceMember member = await workspaces.AddMemberAsync(
    workspace.Id, userId, ["BillingAdmin", "Developer"]);
CloudWorkspaceInvitation invitation = await workspaces.InviteAsync(
    workspace.Id, "developer@example.com", ownerUserId,
    DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
""";

    public const string Subscriptions = """
await subscriptions.SaveAsync(new CloudSubscription
{
    WorkspaceId = workspaceId,
    Application = "cloud-business",
    Reference = "team-growth",
    AutoRenew = true,
    Provider = "MyFatoorah",
    ProviderReference = providerSubscriptionId,
    Metadata = applicationOwnedMetadata
});

bool active = await subscriptions.HasActiveAsync(
    "cloud-business", "team-growth", workspaceId: workspaceId);
""";

    public const string Billing = """
await accountStore.SaveBillingProfileAsync(new CloudBillingProfile
{
    WorkspaceId = workspaceId,
    ProviderCustomerReference = providerCustomerId,
    PaymentMethods =
    [
        new("MyFatoorah", providerPaymentMethodId, "KNET", IsDefault: true)
    ]
});

// CloudLogin stores references only. Execute charges through CloudPayments.
""";
}
