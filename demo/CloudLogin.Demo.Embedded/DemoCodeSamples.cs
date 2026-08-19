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

    public const string Organizations = """
        @inject IOrganizationRegistry Organizations

        CloudLoginOrganization organization =
            await Organizations.CreateAsync("Cedar Labs", ownerUserId);

        await Organizations.AddMemberAsync(
            organization.Id,
            memberUserId,
            roles: ["Developer", "BillingAdmin"]);

        CloudLoginOrganizationInvitation invitation =
            await Organizations.InviteAsync(
                organization.Id,
                "developer@example.com",
                ownerUserId,
                DateTimeOffset.UtcNow.AddDays(7),
                roles: ["Developer"]);
        """;

    public const string Subscriptions = """
        @inject ISubscriptionRegistry Subscriptions

        await Subscriptions.SaveAsync(new AccountSubscription
        {
            OrganizationId = organizationId,
            Application = "cloud-ai",
            Reference = "team-pro",
            Status = AccountSubscriptionStatuses.Active,
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
            "cloud-ai", "team-pro", organizationId: organizationId);
        """;

    public const string Billing = """
        @inject ICloudLoginAccountStore Accounts

        await Accounts.SaveBillingProfileAsync(new AccountBillingProfile
        {
            OrganizationId = organizationId,
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
