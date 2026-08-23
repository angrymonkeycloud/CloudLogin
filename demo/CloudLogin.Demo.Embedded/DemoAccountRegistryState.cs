using System.Text.Json;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;

namespace CloudLogin.Demo.Embedded;

public sealed class DemoAccountRegistryState(ICloudLoginWorkspaceRegistry workspaces, ICloudLoginSubscriptionRegistry subscriptions, ICloudLoginAccountStore accounts)
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly List<CloudWorkspace> _workspaces = [];
    private readonly List<CloudWorkspaceMember> _members = [];
    private readonly List<CloudWorkspaceInvitation> _invitations = [];
    private bool _initialized;

    public Guid OwnerUserId { get; } = Guid.NewGuid();
    public IReadOnlyList<CloudWorkspace> Workspaces => _workspaces;
    public IReadOnlyList<CloudWorkspaceMember> Members => _members;
    public IReadOnlyList<CloudWorkspaceInvitation> Invitations => _invitations;
    public Guid? SelectedWorkspaceId { get; private set; }
    public CloudWorkspace? SelectedWorkspace => _workspaces.FirstOrDefault(workspace => workspace.Id == SelectedWorkspaceId);

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", OwnerUserId);
            _workspaces.Add(workspace);
            SelectedWorkspaceId = workspace.Id;

            await AddMemberAsync(Guid.NewGuid(), ["BillingAdmin", "Developer"], ["billing.manage", "subscriptions.read"]);
            await AddMemberAsync(Guid.NewGuid(), ["Support"], ["members.read"]);

            CloudWorkspaceInvitation invitation = await workspaces.InviteAsync(workspace.Id, "partner@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
            _invitations.Add(invitation);

            await subscriptions.SaveAsync(new CloudSubscription
            {
                UserId = OwnerUserId,
                Application = "cloud-studio",
                Reference = "creator-pro",
                Status = CloudSubscriptionStatuses.Active,
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

            await subscriptions.SaveAsync(new CloudSubscription
            {
                WorkspaceId = workspace.Id,
                Application = "cloud-business",
                Reference = "team-growth",
                Status = CloudSubscriptionStatuses.Active,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(45),
                AutoRenew = true,
                Provider = "MyFatoorah",
                ProviderReference = "sub_demo_workspace",
                Metadata =
                {
                    ["seats"] = JsonSerializer.SerializeToElement(12),
                    ["regions"] = JsonSerializer.SerializeToElement(new[] { "AE", "QA", "LB" })
                }
            });

            await accounts.SaveBillingProfileAsync(new CloudBillingProfile
            {
                WorkspaceId = workspace.Id,
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

    public async Task<CloudWorkspace> CreateWorkspaceAsync(string name)
    {
        CloudWorkspace workspace = await workspaces.CreateAsync(name, OwnerUserId);
        _workspaces.Add(workspace);
        await SelectWorkspaceAsync(workspace.Id);
        return workspace;
    }

    public async Task SelectWorkspaceAsync(Guid workspaceId)
    {
        SelectedWorkspaceId = workspaceId;
        await RefreshMembersAsync();
    }

    public async Task<CloudWorkspaceMember> AddMemberAsync(Guid userId, IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        if (SelectedWorkspaceId is not Guid workspaceId)
            throw new InvalidOperationException("Select a workspace before adding members.");

        _ = await workspaces.AddMemberAsync(workspaceId, userId, roles);
        CloudWorkspaceMember member = new()
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Roles = roles,
            Permissions = permissions
        };
        await accounts.SaveMemberAsync(member);
        await RefreshMembersAsync();
        return member;
    }

    public async Task<CloudWorkspaceInvitation> InviteAsync(string recipient, IReadOnlyList<string> roles)
    {
        if (SelectedWorkspaceId is not Guid workspaceId)
            throw new InvalidOperationException("Select a workspace before creating an invitation.");

        CloudWorkspaceInvitation invitation = await workspaces.InviteAsync(workspaceId, recipient, OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), roles);
        _invitations.Add(invitation);
        return invitation;
    }

    public Task<CloudSubscription> SaveSubscriptionAsync(CloudSubscription subscription) => subscriptions.SaveAsync(subscription);

    public Task<bool> HasActiveSubscriptionAsync(string application, string reference, bool workspaceScope)
        => subscriptions.HasActiveAsync(application, reference, workspaceScope ? null : OwnerUserId, workspaceScope ? SelectedWorkspaceId : null);

    public Task<IReadOnlyList<CloudSubscription>> GetSubscriptionsAsync(bool workspaceScope)
        => accounts.GetSubscriptionsAsync(workspaceScope ? null : OwnerUserId, workspaceScope ? SelectedWorkspaceId : null);

    public Task SaveBillingProfileAsync(CloudBillingProfile profile) => accounts.SaveBillingProfileAsync(profile);

    public Task<CloudBillingProfile?> GetBillingProfileAsync(bool workspaceScope)
        => accounts.GetBillingProfileAsync(workspaceScope ? null : OwnerUserId, workspaceScope ? SelectedWorkspaceId : null);

    private async Task RefreshMembersAsync()
    {
        _members.Clear();
        if (SelectedWorkspaceId is Guid workspaceId)
            _members.AddRange(await workspaces.GetMembersAsync(workspaceId));
    }
}
