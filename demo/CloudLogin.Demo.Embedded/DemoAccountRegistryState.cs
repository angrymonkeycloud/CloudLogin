using System.Text.Json;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;

namespace CloudLogin.Demo.Embedded;

public sealed class DemoAccountRegistryState(ICloudLoginWorkspaceRegistry workspaces, ICloudLoginAccountStore accounts)
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
    public CloudWorkspace? SelectedWorkspace => _workspaces.FirstOrDefault(workspace => workspace.ID == SelectedWorkspaceId);

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
            SelectedWorkspaceId = workspace.ID;

            await AddMemberAsync(Guid.NewGuid(), ["BillingAdmin", "Developer"], ["billing.manage", "members.read"]);
            await AddMemberAsync(Guid.NewGuid(), ["Support"], ["members.read"]);

            CloudWorkspaceInvitation invitation = await workspaces.InviteAsync(workspace.ID, "partner@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
            _invitations.Add(invitation);

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
        await SelectWorkspaceAsync(workspace.ID);
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

    private async Task RefreshMembersAsync()
    {
        _members.Clear();
        if (SelectedWorkspaceId is Guid workspaceId)
            _members.AddRange(await workspaces.GetMembersAsync(workspaceId));
    }
}
