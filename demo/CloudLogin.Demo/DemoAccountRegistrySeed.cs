using System.Text.Json;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;

namespace CloudLogin.Demo;

public sealed class DemoAccountRegistrySeed(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public Guid OwnerUserId { get; } = Guid.NewGuid();
    public IReadOnlyList<CloudLoginOrganization> Organizations { get; private set; } = [];
    public IReadOnlyList<CloudLoginOrganizationInvitation> Invitations { get; private set; } = [];

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _gate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            using IServiceScope scope = scopeFactory.CreateScope();
            IOrganizationRegistry organizations = scope.ServiceProvider.GetRequiredService<IOrganizationRegistry>();
            ISubscriptionRegistry subscriptions = scope.ServiceProvider.GetRequiredService<ISubscriptionRegistry>();
            ICloudLoginAccountStore accounts = scope.ServiceProvider.GetRequiredService<ICloudLoginAccountStore>();

            CloudLoginOrganization cedarLabs = await organizations.CreateAsync("Cedar Labs", OwnerUserId);
            CloudLoginOrganization northstarClinic = await organizations.CreateAsync("Northstar Clinic", OwnerUserId);
            Organizations = [cedarLabs, northstarClinic];

            await AddMemberAsync(accounts, organizations, cedarLabs.Id, ["BillingAdmin", "Developer"], ["billing.manage", "subscriptions.read"]);
            await AddMemberAsync(accounts, organizations, cedarLabs.Id, ["Support"], ["members.read", "invitations.create"]);
            await AddMemberAsync(accounts, organizations, northstarClinic.Id, ["Scheduler"], ["appointments.manage", "members.read"]);

            CloudLoginOrganizationInvitation cedarInvitation = await organizations.InviteAsync(cedarLabs.Id, "partner@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
            CloudLoginOrganizationInvitation clinicInvitation = await organizations.InviteAsync(northstarClinic.Id, "doctor@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(3), ["Practitioner"]);
            Invitations = [cedarInvitation, clinicInvitation];

            await subscriptions.SaveAsync(new()
            {
                UserId = OwnerUserId,
                Application = "cloud-studio",
                Reference = "creator-pro",
                Status = AccountSubscriptionStatuses.Active,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(30),
                AutoRenew = true,
                Provider = "Stripe",
                ProviderReference = "sub_demo_creator",
                Metadata =
                {
                    ["credits"] = JsonSerializer.SerializeToElement(10_000),
                    ["premiumModels"] = JsonSerializer.SerializeToElement(true)
                }
            });
            await subscriptions.SaveAsync(new()
            {
                UserId = OwnerUserId,
                Application = "cloud-studio",
                Reference = "starter-2025",
                Status = AccountSubscriptionStatuses.Expired,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(-40),
                Provider = "Stripe",
                ProviderReference = "sub_demo_expired"
            });
            await subscriptions.SaveAsync(new()
            {
                OrganizationId = cedarLabs.Id,
                Application = "cloud-business",
                Reference = "team-growth",
                Status = AccountSubscriptionStatuses.Active,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(45),
                AutoRenew = true,
                Provider = "MyFatoorah",
                ProviderReference = "sub_demo_cedar",
                Metadata =
                {
                    ["seats"] = JsonSerializer.SerializeToElement(12),
                    ["regions"] = JsonSerializer.SerializeToElement(new[] { "AE", "QA", "LB" })
                }
            });
            await subscriptions.SaveAsync(new()
            {
                OrganizationId = northstarClinic.Id,
                Application = "clinic-appointments",
                Reference = "practice-plus",
                Status = AccountSubscriptionStatuses.Active,
                AutoRenew = true,
                Provider = "SkipCash",
                ProviderReference = "sub_demo_clinic",
                Metadata =
                {
                    ["practitioners"] = JsonSerializer.SerializeToElement(8),
                    ["locations"] = JsonSerializer.SerializeToElement(2)
                }
            });

            await accounts.SaveBillingProfileAsync(new()
            {
                UserId = OwnerUserId,
                ProviderCustomerReference = "cus_demo_owner",
                PaymentMethods = [new("Stripe", "pm_demo_visa", "Visa ending 4242", true)]
            });
            await accounts.SaveBillingProfileAsync(new()
            {
                OrganizationId = cedarLabs.Id,
                ProviderCustomerReference = "cus_demo_cedar",
                PaymentMethods =
                [
                    new("MyFatoorah", "pm_demo_knet", "KNET sandbox", true),
                    new("Stripe", "pm_demo_mastercard", "Mastercard ending 4444")
                ]
            });
            await accounts.SaveBillingProfileAsync(new()
            {
                OrganizationId = northstarClinic.Id,
                ProviderCustomerReference = "cus_demo_northstar",
                PaymentMethods = [new("SkipCash", "token_demo_qatar", "SkipCash sandbox", true)]
            });

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task AddMemberAsync(ICloudLoginAccountStore accounts, IOrganizationRegistry organizations, Guid organizationId, IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        Guid userId = Guid.NewGuid();
        await organizations.AddMemberAsync(organizationId, userId, roles);
        await accounts.SaveMemberAsync(new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Roles = roles,
            Permissions = permissions
        });
    }
}
