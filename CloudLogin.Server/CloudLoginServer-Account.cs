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

    public async Task<OrganizationQuota> GetMyOrganizationQuota()
    {
        OrganizationConfiguration options = _configuration.Organization ?? new OrganizationConfiguration();

        // An unconfigured registry or a signed-out caller still reports the configured caps, so
        // the account UI describes the same allowance it would enforce.
        OrganizationQuota empty = new()
        {
            Owned = 0,
            MaxOwned = options.EffectiveMaxOwnedPerUser,
            Total = 0,
            MaxTotal = options.EffectiveMaxPerUser
        };

        if (_organizationRegistry == null)
            return empty;

        UserModel? user = await CurrentUser();

        return user == null ? empty : await _organizationRegistry.GetQuotaAsync(user.ID);
    }

    public async Task<List<AccountSubscription>> GetMySubscriptions(bool includeInactive = false)
    {
        if (_subscriptionRegistry == null)
            return [];

        UserModel? user = await CurrentUser();

        if (user == null)
            return [];

        return includeInactive
            ? [.. await _subscriptionRegistry.GetForOwnerAsync(userId: user.ID)]
            : [.. await _subscriptionRegistry.GetActiveAsync(userId: user.ID)];
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

        await RequireOrganizationManagerAsync(organizationId, user.ID);

        return await _organizationRegistry.InviteAsync(organizationId, recipient, user.ID, DateTimeOffset.UtcNow.AddDays(7), roles);
    }

    public async Task<CloudLoginOrganization> UpdateOrganization(CloudLoginOrganization organization)
    {
        if (_organizationRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        return await _organizationRegistry.UpdateAsync(organization, user.ID);
    }

    public async Task DeleteOrganization(Guid organizationId)
    {
        if (_organizationRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        await _organizationRegistry.DeleteAsync(organizationId, user.ID);
    }

    /// <summary>
    /// Everything the account UI renders for one organization the caller belongs to: its profile,
    /// the caller's standing, members, subscriptions, and billing. Returns null when the caller
    /// isn't a member, so a guessed identifier can't confirm an organization exists.
    /// </summary>
    public async Task<OrganizationWorkspace?> GetOrganizationWorkspace(Guid organizationId)
    {
        if (_organizationRegistry == null)
            return null;

        UserModel? user = await CurrentUser();

        if (user == null)
            return null;

        CloudLoginOrganization? organization = await _organizationRegistry.GetAsync(organizationId);

        if (organization == null)
            return null;

        IReadOnlyList<CloudLoginOrganizationMember> members = await _organizationRegistry.GetMembersAsync(organizationId);
        CloudLoginOrganizationMember? membership = members.FirstOrDefault(member => member.UserId == user.ID);

        bool isOwner = organization.OwnerUserId == user.ID
            || membership is { IsOwner: true }
            || HasRole(membership, "Owner");

        if (membership == null && !isOwner)
            return null;

        bool canManage = isOwner || HasRole(membership, "Admin");

        IReadOnlyList<AccountSubscription> subscriptions = _subscriptionRegistry == null
            ? []
            : await _subscriptionRegistry.GetForOwnerAsync(organizationId: organizationId);

        AccountBillingProfile? billing = null;

        // Billing details name the people and the account that pay for the organization, so they
        // stay with the owner and admins rather than every member.
        if (canManage && _accountStore != null)
            billing = await _accountStore.GetBillingProfileAsync(null, organizationId);

        OrganizationDeletionReport? deletion = isOwner
            ? await _organizationRegistry.GetDeletionReportAsync(organizationId, user.ID)
            : null;

        return new OrganizationWorkspace
        {
            Organization = organization,
            IsOwner = isOwner,
            CanManage = canManage,
            Roles = membership?.Roles ?? (isOwner ? ["Owner"] : []),
            Members = await DescribeMembersAsync(organization, members),
            Subscriptions = subscriptions,
            BillingProfile = billing,
            Deletion = deletion
        };
    }

    /// <summary>
    /// Names and pictures the members of an organization for its members list. Only the display
    /// name, primary email, and avatar cross over; a host without a user store, or a membership
    /// whose user record is gone, still renders as a row rather than failing the whole workspace.
    /// </summary>
    private async Task<List<OrganizationMemberProfile>> DescribeMembersAsync(CloudLoginOrganization organization, IReadOnlyList<CloudLoginOrganizationMember> members)
    {
        List<OrganizationMemberProfile> profiles = [];

        foreach (CloudLoginOrganizationMember member in members)
        {
            UserModel? user = null;

            if (_cosmosMethods != null)
                try
                {
                    user = await _cosmosMethods.GetUserById(member.UserId);
                }
                catch (Exception) { }

            profiles.Add(new OrganizationMemberProfile
            {
                UserId = member.UserId,
                DisplayName = user?.DisplayName ?? string.Join(" ", new[] { user?.FirstName, user?.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))),
                EmailAddress = (user?.PrimaryEmailAddress ?? user?.EmailAddresses.FirstOrDefault())?.Input,
                ProfilePicture = user?.ProfilePicture,
                Roles = member.Roles,
                IsOwner = member.IsOwner || organization.OwnerUserId == member.UserId,
                State = member.State,
                JoinedOn = member.JoinedOn
            });
        }

        // Owners first, then the longest-standing members.
        return [.. profiles.OrderByDescending(profile => profile.IsOwner).ThenBy(profile => profile.JoinedOn)];
    }

    public async Task DeleteSubscription(Guid subscriptionId)
    {
        if (_subscriptionRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        if (_configuration.Subscription is { AllowSelfServiceDeletion: false })
            throw new InvalidOperationException("Subscriptions on this host are managed by the application that created them.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        AccountSubscription subscription = await _subscriptionRegistry.GetAsync(subscriptionId)
            ?? throw new KeyNotFoundException($"Subscription '{subscriptionId}' was not found.");

        if (subscription.OrganizationId is Guid organizationId)
            await RequireOrganizationManagerAsync(organizationId, user.ID);
        else if (subscription.UserId != user.ID)
            throw new UnauthorizedAccessException("This subscription belongs to another account.");

        await _subscriptionRegistry.DeleteAsync(subscriptionId);
    }

    public async Task<AccountBillingProfile> AddPaymentMethod(AccountPaymentMethodReference method, Guid? organizationId = null)
    {
        if (_accountStore == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        if (organizationId is Guid targetOrganizationId)
            await RequireOrganizationManagerAsync(targetOrganizationId, user.ID);

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

    public async Task<AccountBillingProfile> RemovePaymentMethod(string provider, string reference, Guid? organizationId = null)
    {
        if (_accountStore == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        UserModel user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        if (organizationId is Guid targetOrganizationId)
            await RequireOrganizationManagerAsync(targetOrganizationId, user.ID);

        Guid? userId = organizationId is null ? user.ID : null;
        AccountBillingProfile? existing = await _accountStore.GetBillingProfileAsync(userId, organizationId)
            ?? throw new KeyNotFoundException("No billing profile exists for this account.");

        List<AccountPaymentMethodReference> methods = [.. existing.PaymentMethods];

        if (methods.RemoveAll(m => m.Provider == provider && m.Reference == reference) == 0)
            throw new KeyNotFoundException("That payment method is not saved on this account.");

        // The account should never be left with saved methods but no default one.
        if (methods.Count > 0 && !methods.Any(m => m.IsDefault))
            methods[0] = methods[0] with { IsDefault = true };

        AccountBillingProfile profile = new()
        {
            UserId = userId,
            OrganizationId = organizationId,
            ProviderCustomerReference = existing.ProviderCustomerReference,
            PaymentMethods = methods,
            Metadata = existing.Metadata
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

    /// <summary>
    /// Throws unless the user owns the organization or holds its Admin role. Every write that
    /// targets an organization &mdash; invitations, billing, subscription removal &mdash; passes
    /// through here, so an identifier alone never grants access to someone else's organization.
    /// </summary>
    private async Task RequireOrganizationManagerAsync(Guid organizationId, Guid userId)
    {
        if (_organizationRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudLoginOrganization organization = await _organizationRegistry.GetAsync(organizationId)
            ?? throw new KeyNotFoundException($"Organization '{organizationId}' was not found.");

        if (organization.OwnerUserId == userId)
            return;

        IReadOnlyList<CloudLoginOrganizationMember> members = await _organizationRegistry.GetMembersAsync(organizationId);
        CloudLoginOrganizationMember? membership = members.FirstOrDefault(member => member.UserId == userId);

        if (membership is { IsOwner: true } || HasRole(membership, "Owner") || HasRole(membership, "Admin"))
            return;

        throw new UnauthorizedAccessException("Only the organization's owner or an admin member may do this.");
    }

    private static bool HasRole(CloudLoginOrganizationMember? member, string role)
        => member?.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) ?? false;
}
