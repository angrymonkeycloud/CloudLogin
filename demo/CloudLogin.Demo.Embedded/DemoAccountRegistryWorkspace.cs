using System.Text.Json;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;

namespace CloudLogin.Demo.Embedded;

public sealed class DemoAccountRegistryWorkspace(IOrganizationRegistry organizations, ISubscriptionRegistry subscriptions, ICloudLoginAccountStore accounts)
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly List<CloudLoginOrganization> _organizations = [];
    private readonly List<CloudLoginOrganizationMember> _members = [];
    private readonly List<CloudLoginOrganizationInvitation> _invitations = [];
    private bool _initialized;

    public Guid OwnerUserId { get; } = Guid.NewGuid();
    public IReadOnlyList<CloudLoginOrganization> Organizations => _organizations;
    public IReadOnlyList<CloudLoginOrganizationMember> Members => _members;
    public IReadOnlyList<CloudLoginOrganizationInvitation> Invitations => _invitations;
    public Guid? SelectedOrganizationId { get; private set; }
    public CloudLoginOrganization? SelectedOrganization => _organizations.FirstOrDefault(organization => organization.Id == SelectedOrganizationId);

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", OwnerUserId);
            _organizations.Add(organization);
            SelectedOrganizationId = organization.Id;

            await AddMemberAsync(Guid.NewGuid(), ["BillingAdmin", "Developer"], ["billing.manage", "subscriptions.read"]);
            await AddMemberAsync(Guid.NewGuid(), ["Support"], ["members.read"]);

            CloudLoginOrganizationInvitation invitation = await organizations.InviteAsync(organization.Id, "partner@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
            _invitations.Add(invitation);

            await subscriptions.SaveAsync(new AccountSubscription
            {
                UserId = OwnerUserId,
                Application = "cloud-studio",
                Reference = "creator-pro",
                Status = AccountSubscriptionStatuses.Active,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(30),
                AutoRenew = true,
                Provider = "Stripe",
                ProviderReference = "sub_demo_user",
                Metadata =
                {
                    ["credits"] = JsonSerializer.SerializeToElement(10_000),
                    ["premiumModels"] = JsonSerializer.SerializeToElement(true)
                }
            });

            await subscriptions.SaveAsync(new AccountSubscription
            {
                OrganizationId = organization.Id,
                Application = "cloud-business",
                Reference = "team-growth",
                Status = AccountSubscriptionStatuses.Active,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(45),
                AutoRenew = true,
                Provider = "MyFatoorah",
                ProviderReference = "sub_demo_organization",
                Metadata =
                {
                    ["seats"] = JsonSerializer.SerializeToElement(12),
                    ["regions"] = JsonSerializer.SerializeToElement(new[] { "AE", "QA", "LB" })
                }
            });

            await accounts.SaveBillingProfileAsync(new AccountBillingProfile
            {
                OrganizationId = organization.Id,
                ProviderCustomerReference = "cus_demo_cedar",
                PaymentMethods =
                [
                    new("Stripe", "pm_demo_visa", "Visa ending 4242", IsDefault: true),
                    new("MyFatoorah", "pm_demo_knet", "KNET sandbox reference")
                ]
            });

            await RefreshMembersAsync();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<CloudLoginOrganization> CreateOrganizationAsync(string name)
    {
        CloudLoginOrganization organization = await organizations.CreateAsync(name, OwnerUserId);
        _organizations.Add(organization);
        await SelectOrganizationAsync(organization.Id);
        return organization;
    }

    public async Task SelectOrganizationAsync(Guid organizationId)
    {
        SelectedOrganizationId = organizationId;
        await RefreshMembersAsync();
    }

    public async Task<CloudLoginOrganizationMember> AddMemberAsync(Guid userId, IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        if (SelectedOrganizationId is not Guid organizationId)
            throw new InvalidOperationException("Select an organization before adding members.");

        _ = await organizations.AddMemberAsync(organizationId, userId, roles);
        CloudLoginOrganizationMember member = new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Roles = roles,
            Permissions = permissions
        };
        await accounts.SaveMemberAsync(member);
        await RefreshMembersAsync();
        return member;
    }

    public async Task<CloudLoginOrganizationInvitation> InviteAsync(string recipient, IReadOnlyList<string> roles)
    {
        if (SelectedOrganizationId is not Guid organizationId)
            throw new InvalidOperationException("Select an organization before creating an invitation.");

        CloudLoginOrganizationInvitation invitation = await organizations.InviteAsync(organizationId, recipient, OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), roles);
        _invitations.Add(invitation);
        return invitation;
    }

    public Task<AccountSubscription> SaveSubscriptionAsync(AccountSubscription subscription) => subscriptions.SaveAsync(subscription);

    public Task<bool> HasActiveSubscriptionAsync(string application, string reference, bool organizationScope)
        => subscriptions.HasActiveAsync(application, reference, organizationScope ? null : OwnerUserId, organizationScope ? SelectedOrganizationId : null);

    public Task<IReadOnlyList<AccountSubscription>> GetSubscriptionsAsync(bool organizationScope)
        => accounts.GetSubscriptionsAsync(organizationScope ? null : OwnerUserId, organizationScope ? SelectedOrganizationId : null);

    public Task SaveBillingProfileAsync(AccountBillingProfile profile) => accounts.SaveBillingProfileAsync(profile);

    public Task<AccountBillingProfile?> GetBillingProfileAsync(bool organizationScope)
        => accounts.GetBillingProfileAsync(organizationScope ? null : OwnerUserId, organizationScope ? SelectedOrganizationId : null);

    private async Task RefreshMembersAsync()
    {
        _members.Clear();
        if (SelectedOrganizationId is Guid organizationId)
            _members.AddRange(await organizations.GetMembersAsync(organizationId));
    }
}
