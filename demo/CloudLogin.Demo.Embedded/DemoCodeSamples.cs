namespace CloudLogin.Demo.Embedded;

public static class DemoCodeSamples
{
    public const string EmbeddedRegistration = """
        builder.Services.AddCloudLoginAccountRegistry();

        builder.Services.AddCloudLoginEmbedded(
            new CloudLoginWebConfiguration
            {
                Providers =
                [
                    new LoginProviders.PasswordProviderConfiguration(
                        configuration.GetSection("Password")),
                    new LoginProviders.CodeProviderConfiguration(
                        configuration.GetSection("Code")),
                    new LoginTestProviders.TestModeConfiguration(
                        configuration.GetSection("TestMode"))
                ]
            },
            configuration);
        """;

    public const string Login = """
        @page "/login"

        <CloudLoginPage Embedded="true" />
        """;

    public const string Account = """
        @page "/account"

        <AccountPageComponent />
        """;

    public const string Workspaces = """
        @inject ICloudLoginWorkspaceRegistry Workspaces

        CloudWorkspace workspace =
            await Workspaces.CreateAsync("Cedar Labs", ownerUserId);

        await Workspaces.AddMemberAsync(
            workspace.Id,
            memberUserId,
            roles: ["Developer", "BillingAdmin"]);

        CloudWorkspaceInvitation invitation =
            await Workspaces.InviteAsync(
                workspace.Id,
                "developer@example.com",
                ownerUserId,
                DateTimeOffset.UtcNow.AddDays(7),
                roles: ["Developer"]);
        """;

    public const string Subscriptions = """
        @inject ICloudLoginSubscriptionRegistry Subscriptions

        await Subscriptions.SaveAsync(new CloudSubscription
        {
            WorkspaceId = workspaceId,
            Application = "cloud-ai",
            Reference = "team-pro",
            Status = CloudSubscriptionStatuses.Active,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(30),
            AutoRenew = true,
            Provider = "Stripe",
            ProviderReference = "sub_provider_reference",
            Metadata =
            {
                ["credits"] = JsonSerializer.SerializeToElement(10_000),
                ["premiumModels"] = JsonSerializer.SerializeToElement(true)
            }
        });

        bool active = await Subscriptions.HasActiveAsync(
            "cloud-ai", "team-pro", workspaceId: workspaceId);
        """;

    public const string Billing = """
        @inject ICloudLoginAccountStore Accounts

        await Accounts.SaveBillingProfileAsync(new CloudBillingProfile
        {
            WorkspaceId = workspaceId,
            ProviderCustomerReference = "cus_provider_reference",
            PaymentMethods =
            [
                new("Stripe", "pm_provider_token", "Visa ending 4242", IsDefault: true)
            ]
        });

        // CloudLogin stores references. A payment package executes transactions.
        """;

    public const string Persistence = """
        services.AddCloudLoginAccountRegistry();

        // The demo uses the built-in in-memory store.
        // Production infrastructure replaces only this public contract:
        services.AddSingleton<ICloudLoginAccountStore, MyAccountStore>();
        """;
}
