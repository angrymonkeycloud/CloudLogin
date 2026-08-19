namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer : Interfaces.ICloudLogin
{
    public async Task<List<CloudLoginOrganization>> GetMyOrganizations()
    {
        if (_organizationRegistry == null)
            return [];

        UserModel? user = await CurrentUser();

        if (user == null)
            return [];

        return [.. await _organizationRegistry.GetOrganizationsForUserAsync(user.ID)];
    }

    public async Task<List<AccountSubscription>> GetMySubscriptions()
    {
        if (_subscriptionRegistry == null)
            return [];

        UserModel? user = await CurrentUser();

        if (user == null)
            return [];

        return [.. await _subscriptionRegistry.GetActiveAsync(userId: user.ID)];
    }

    public async Task<AccountBillingProfile?> GetMyBillingProfile()
    {
        if (_accountStore == null)
            return null;

        UserModel? user = await CurrentUser();

        if (user == null)
            return null;

        return await _accountStore.GetBillingProfileAsync(userId: user.ID, organizationId: null);
    }

    public async Task<CloudLoginOrganization> CreateOrganization(string name)
    {
        if (_organizationRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        return await _organizationRegistry.CreateAsync(name, user.ID);
    }

    public async Task<CloudLoginOrganizationInvitation> InviteToOrganization(Guid organizationId, string recipient, IReadOnlyList<string>? roles = null)
    {
        if (_organizationRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        return await _organizationRegistry.InviteAsync(organizationId, recipient, user.ID, DateTimeOffset.UtcNow.AddDays(7), roles);
    }

    public async Task<CloudLoginOrganization> UpdateOrganization(CloudLoginOrganization organization)
    {
        if (_organizationRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        return await _organizationRegistry.UpdateAsync(organization, user.ID);
    }

    public async Task<AccountBillingProfile> AddPaymentMethod(AccountPaymentMethodReference method, Guid? organizationId = null)
    {
        if (_accountStore == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        Guid? userId = organizationId is null ? user.ID : null;
        AccountBillingProfile? existing = await _accountStore.GetBillingProfileAsync(userId, organizationId);

        List<AccountPaymentMethodReference> methods = existing?.PaymentMethods.ToList() ?? [];
        methods.RemoveAll(m => m.Provider == method.Provider && m.Reference == method.Reference);

        if (method.IsDefault)
            methods = [.. methods.Select(m => m with { IsDefault = false })];

        methods.Add(method);

        AccountBillingProfile profile = new()
        {
            UserId = userId,
            OrganizationId = organizationId,
            ProviderCustomerReference = existing?.ProviderCustomerReference,
            PaymentMethods = methods,
            Metadata = existing?.Metadata ?? []
        };

        await _accountStore.SaveBillingProfileAsync(profile);
        return profile;
    }


    public async Task<List<CloudLoginOrganizationMember>> GetOrganizationMembers(Guid organizationId)
    {
        if (_organizationRegistry == null)
            return [];

        return [.. await _organizationRegistry.GetMembersAsync(organizationId)];
    }
    public async Task<CloudLoginOrganization?> GetOrganizationById(Guid organizationId)
    {
        if (_organizationRegistry == null)
            return null;

        return await _organizationRegistry.GetAsync(organizationId);
    }

    public async Task<List<CloudLoginOrganization>> GetAllOrganizations()
    {
        if (_organizationRegistry == null)
            return [];

        return [.. await _organizationRegistry.GetAllAsync()];
    }

    public async Task<AccountSubscription?> GetSubscriptionById(Guid subscriptionId)
    {
        if (_subscriptionRegistry == null)
            return null;

        return await _subscriptionRegistry.GetAsync(subscriptionId);
    }

    public async Task<List<AccountSubscription>> GetAllSubscriptions()
    {
        if (_subscriptionRegistry == null)
            return [];

        return [.. await _subscriptionRegistry.GetAllAsync()];
    }
}
