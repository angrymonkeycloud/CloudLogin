namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer : Interfaces.ICloudLogin
{
    public async Task<List<CloudWorkspace>> GetMyWorkspaces()
    {
        if (_workspaceRegistry == null)
            return [];

        CloudUser? user = await CurrentUser();

        if (user == null)
            return [];

        return [.. await _workspaceRegistry.GetWorkspacesForUserAsync(user.ID)];
    }

    public async Task<CloudWorkspaceQuota> GetMyWorkspaceQuota()
    {
        WorkspaceConfiguration options = _configuration.Workspace ?? new WorkspaceConfiguration();

        // An unconfigured registry or a signed-out caller still reports the configured caps, so
        // the account UI describes the same allowance it would enforce.
        CloudWorkspaceQuota empty = new()
        {
            Owned = 0,
            MaxOwned = options.EffectiveMaxOwnedPerUser,
            Total = 0,
            MaxTotal = options.EffectiveMaxPerUser
        };

        if (_workspaceRegistry == null)
            return empty;

        CloudUser? user = await CurrentUser();

        return user == null ? empty : await _workspaceRegistry.GetQuotaAsync(user.ID);
    }

    public async Task<List<CloudSubscription>> GetMySubscriptions(bool includeInactive = false)
    {
        if (_subscriptionRegistry == null)
            return [];

        CloudUser? user = await CurrentUser();

        if (user == null)
            return [];

        return includeInactive
            ? [.. await _subscriptionRegistry.GetForOwnerAsync(userId: user.ID)]
            : [.. await _subscriptionRegistry.GetActiveAsync(userId: user.ID)];
    }

    public async Task<CloudBillingProfile?> GetMyBillingProfile()
    {
        if (_accountStore == null)
            return null;

        CloudUser? user = await CurrentUser();

        if (user == null)
            return null;

        return await _accountStore.GetBillingProfileAsync(userId: user.ID, workspaceId: null);
    }

    public async Task<CloudWorkspace> CreateWorkspace(string name)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        return await _workspaceRegistry.CreateAsync(name, user.ID);
    }

    public async Task<CloudWorkspaceInvitation> InviteToWorkspace(Guid workspaceId, string recipient, IReadOnlyList<string>? roles = null)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        await RequireWorkspaceManagerAsync(workspaceId, user.ID);

        return await _workspaceRegistry.InviteAsync(workspaceId, recipient, user.ID, DateTimeOffset.UtcNow.AddDays(7), roles);
    }

    public async Task<CloudWorkspace> UpdateWorkspace(CloudWorkspace workspace)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        return await _workspaceRegistry.UpdateAsync(workspace, user.ID);
    }

    public async Task DeleteWorkspace(Guid workspaceId)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        await _workspaceRegistry.DeleteAsync(workspaceId, user.ID);
    }

    /// <summary>
    /// Everything the account UI renders for one workspace the caller belongs to: its profile,
    /// the caller's standing, members, subscriptions, and billing. Returns null when the caller
    /// isn't a member, so a guessed identifier can't confirm a workspace exists.
    /// </summary>
    public async Task<CloudWorkspaceDetail?> GetWorkspaceDetail(Guid workspaceId)
    {
        if (_workspaceRegistry == null)
            return null;

        CloudUser? user = await CurrentUser();

        if (user == null)
            return null;

        CloudWorkspace? workspace = await _workspaceRegistry.GetAsync(workspaceId);

        if (workspace == null)
            return null;

        IReadOnlyList<CloudWorkspaceMember> members = await _workspaceRegistry.GetMembersAsync(workspaceId);
        CloudWorkspaceMember? membership = members.FirstOrDefault(member => member.UserId == user.ID);

        bool isOwner = workspace.OwnerUserId == user.ID
            || membership is { IsOwner: true }
            || HasRole(membership, "Owner");

        if (membership == null && !isOwner)
            return null;

        bool canManage = isOwner || HasRole(membership, "Admin");

        IReadOnlyList<CloudSubscription> subscriptions = _subscriptionRegistry == null
            ? []
            : await _subscriptionRegistry.GetForOwnerAsync(workspaceId: workspaceId);

        CloudBillingProfile? billing = null;

        // Billing details name the people and the account that pay for the workspace, so they
        // stay with the owner and admins rather than every member.
        if (canManage && _accountStore != null)
            billing = await _accountStore.GetBillingProfileAsync(null, workspaceId);

        CloudWorkspaceDeletionReport? deletion = isOwner
            ? await _workspaceRegistry.GetDeletionReportAsync(workspaceId, user.ID)
            : null;

        return new CloudWorkspaceDetail
        {
            Workspace = workspace,
            IsOwner = isOwner,
            CanManage = canManage,
            Roles = membership?.Roles ?? (isOwner ? ["Owner"] : []),
            Members = await DescribeMembersAsync(workspace, members),
            Subscriptions = subscriptions,
            BillingProfile = billing,
            Deletion = deletion
        };
    }

    /// <summary>
    /// Names and pictures the members of a workspace for its members list. Only the display
    /// name, primary email, and avatar cross over; a host without a user store, or a membership
    /// whose user record is gone, still renders as a row rather than failing the whole workspace.
    /// </summary>
    private async Task<List<CloudWorkspaceMemberProfile>> DescribeMembersAsync(CloudWorkspace workspace, IReadOnlyList<CloudWorkspaceMember> members)
    {
        List<CloudWorkspaceMemberProfile> profiles = [];

        foreach (CloudWorkspaceMember member in members)
        {
            CloudUser? user = null;

            if (_cosmosMethods != null)
                try
                {
                    user = await _cosmosMethods.GetUserById(member.UserId);
                }
                catch (Exception) { }

            profiles.Add(new CloudWorkspaceMemberProfile
            {
                UserId = member.UserId,
                DisplayName = user?.DisplayName ?? string.Join(" ", new[] { user?.FirstName, user?.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))),
                EmailAddress = (user?.PrimaryEmailAddress ?? user?.EmailAddresses.FirstOrDefault())?.Input,
                ProfilePicture = user?.ProfilePicture,
                Roles = member.Roles,
                IsOwner = member.IsOwner || workspace.OwnerUserId == member.UserId,
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

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        CloudSubscription subscription = await _subscriptionRegistry.GetAsync(subscriptionId)
            ?? throw new KeyNotFoundException($"Subscription '{subscriptionId}' was not found.");

        if (subscription.WorkspaceId is Guid workspaceId)
            await RequireWorkspaceManagerAsync(workspaceId, user.ID);
        else if (subscription.UserId != user.ID)
            throw new UnauthorizedAccessException("This subscription belongs to another account.");

        await _subscriptionRegistry.DeleteAsync(subscriptionId);
    }

    public async Task<CloudBillingProfile> AddPaymentMethod(CloudPaymentMethodReference method, Guid? workspaceId = null)
    {
        if (_accountStore == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        if (workspaceId is Guid targetWorkspaceId)
            await RequireWorkspaceManagerAsync(targetWorkspaceId, user.ID);

        Guid? userId = workspaceId is null ? user.ID : null;
        CloudBillingProfile? existing = await _accountStore.GetBillingProfileAsync(userId, workspaceId);

        List<CloudPaymentMethodReference> methods = existing?.PaymentMethods.ToList() ?? [];
        methods.RemoveAll(m => m.Provider == method.Provider && m.Reference == method.Reference);

        if (method.IsDefault)
            methods = [.. methods.Select(m => m with { IsDefault = false })];

        methods.Add(method);

        CloudBillingProfile profile = new()
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            ProviderCustomerReference = existing?.ProviderCustomerReference,
            PaymentMethods = methods,
            Metadata = existing?.Metadata ?? []
        };

        await _accountStore.SaveBillingProfileAsync(profile);
        return profile;
    }

    public async Task<CloudBillingProfile> RemovePaymentMethod(string provider, string reference, Guid? workspaceId = null)
    {
        if (_accountStore == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        CloudUser user = await CurrentUser() ?? throw new UnauthorizedAccessException("Sign-in is required.");

        if (workspaceId is Guid targetWorkspaceId)
            await RequireWorkspaceManagerAsync(targetWorkspaceId, user.ID);

        Guid? userId = workspaceId is null ? user.ID : null;
        CloudBillingProfile? existing = await _accountStore.GetBillingProfileAsync(userId, workspaceId)
            ?? throw new KeyNotFoundException("No billing profile exists for this account.");

        List<CloudPaymentMethodReference> methods = [.. existing.PaymentMethods];

        if (methods.RemoveAll(m => m.Provider == provider && m.Reference == reference) == 0)
            throw new KeyNotFoundException("That payment method is not saved on this account.");

        // The account should never be left with saved methods but no default one.
        if (methods.Count > 0 && !methods.Any(m => m.IsDefault))
            methods[0] = methods[0] with { IsDefault = true };

        CloudBillingProfile profile = new()
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            ProviderCustomerReference = existing.ProviderCustomerReference,
            PaymentMethods = methods,
            Metadata = existing.Metadata
        };

        await _accountStore.SaveBillingProfileAsync(profile);
        return profile;
    }

    public async Task<List<CloudWorkspaceMember>> GetWorkspaceMembers(Guid workspaceId)
    {
        if (_workspaceRegistry == null)
            return [];

        return [.. await _workspaceRegistry.GetMembersAsync(workspaceId)];
    }
    public async Task<CloudWorkspace?> GetWorkspaceById(Guid workspaceId)
    {
        if (_workspaceRegistry == null)
            return null;

        return await _workspaceRegistry.GetAsync(workspaceId);
    }

    public async Task<List<CloudWorkspace>> GetAllWorkspaces()
    {
        if (_workspaceRegistry == null)
            return [];

        return [.. await _workspaceRegistry.GetAllAsync()];
    }

    public async Task<CloudSubscription?> GetSubscriptionById(Guid subscriptionId)
    {
        if (_subscriptionRegistry == null)
            return null;

        return await _subscriptionRegistry.GetAsync(subscriptionId);
    }

    public async Task<List<CloudSubscription>> GetAllSubscriptions()
    {
        if (_subscriptionRegistry == null)
            return [];

        return [.. await _subscriptionRegistry.GetAllAsync()];
    }

    /// <summary>
    /// Throws unless the user owns the workspace or holds its Admin role. Every write that
    /// targets a workspace &mdash; invitations, billing, subscription removal &mdash; passes
    /// through here, so an identifier alone never grants access to someone else's workspace.
    /// </summary>
    private async Task RequireWorkspaceManagerAsync(Guid workspaceId, Guid userId)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        CloudWorkspace workspace = await _workspaceRegistry.GetAsync(workspaceId)
            ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");

        if (workspace.OwnerUserId == userId)
            return;

        IReadOnlyList<CloudWorkspaceMember> members = await _workspaceRegistry.GetMembersAsync(workspaceId);
        CloudWorkspaceMember? membership = members.FirstOrDefault(member => member.UserId == userId);

        if (membership is { IsOwner: true } || HasRole(membership, "Owner") || HasRole(membership, "Admin"))
            return;

        throw new UnauthorizedAccessException("Only the workspace's owner or an admin member may do this.");
    }

    private static bool HasRole(CloudWorkspaceMember? member, string role)
        => member?.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) ?? false;
}
