using System.Text.Json;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;

namespace CloudLogin.Demo;

public sealed class DemoAccountRegistrySeed(IServiceScopeFactory scopeFactory, Guid ownerUserId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// The demo admin's user id, so the seeded organizations, subscriptions, and billing
    /// belong to the account you sign in as — the account page shows them straight away
    /// instead of an empty workspace.
    /// </summary>
    public Guid OwnerUserId { get; } = ownerUserId;
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

            // Filled in so the organization workspace has real information and billing details
            // to show, the way a set-up organization would.
            cedarLabs.LegalName = "Cedar Labs SARL";
            cedarLabs.Website = "https://cedarlabs.example";
            cedarLabs.Phone = "+961 1 000 000";
            cedarLabs.BillingEmail = "billing@cedarlabs.example";
            cedarLabs.BillingContactName = "Rita Haddad";
            cedarLabs.TaxId = "LB-1234567";
            cedarLabs.BillingAddress = new OrganizationAddress
            {
                Line1 = "12 Cedar Street",
                Line2 = "4th floor",
                City = "Beirut",
                PostalCode = "1103",
                Country = "Lebanon"
            };
            cedarLabs = await organizations.UpdateAsync(cedarLabs, OwnerUserId);

            northstarClinic.BillingEmail = "accounts@northstar.example";
            northstarClinic.BillingContactName = "Dr. Karim Nasr";
            northstarClinic.BillingAddress = new OrganizationAddress { Line1 = "88 West Bay", City = "Doha", Country = "Qatar" };
            northstarClinic = await organizations.UpdateAsync(northstarClinic, OwnerUserId);

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
            // Expired and under the default policy, so the account page offers to remove it.
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

            // Long expired but marked Never, to show an entry the account holder can't clear and
            // that keeps its organization from being deleted.
            await subscriptions.SaveAsync(new()
            {
                OrganizationId = northstarClinic.Id,
                Application = "clinic-ledger",
                Reference = "audit-2023",
                Status = AccountSubscriptionStatuses.Expired,
                ExpiresOn = DateTimeOffset.UtcNow.AddYears(-1),
                Provider = "SkipCash",
                ProviderReference = "sub_demo_audit",
                DeletionPolicy = SubscriptionDeletionPolicies.Never
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
