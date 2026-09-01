using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class WorkspaceAccessServiceTests
{
    private readonly InMemoryWorkspaceRepository _workspaces = new();
    private readonly InMemoryWorkspaceAccessRepository _access = new();
    private readonly InMemoryUserWorkspaceIndexStore _index = new();
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly WorkspaceAccessService _service;

    private readonly Guid _owner = Guid.NewGuid();

    public WorkspaceAccessServiceTests() => _service = new WorkspaceAccessService(
        _workspaces, _access, _index, _configuration, new AuditLogger(_audit, _configuration));

    private async Task<Guid> CreateWorkspaceAsync()
    {
        WorkspaceDocument workspace = await _service.CreateWorkspaceAsync(_owner, "Acme");
        return Guid.Parse(workspace.Id);
    }

    // ── Multiple owners ───────────────────────────────────────────────────────

    [Fact]
    public async Task Workspace_SupportsMultipleOwners()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        Guid secondOwner = Guid.NewGuid();

        await _service.AddMemberAsync(workspaceId, secondOwner, [WorkspaceRoles.Owner], _owner);

        Assert.Equal(2, await _service.CountActiveOwnersAsync(workspaceId));

        // With two owners, one may leave.
        await _service.RemoveMemberAsync(workspaceId, _owner, _owner);
        Assert.Equal(1, await _service.CountActiveOwnersAsync(workspaceId));
    }

    // ── Final-owner protection ────────────────────────────────────────────────

    [Fact]
    public async Task LastOwner_CannotLeave()
    {
        Guid workspaceId = await CreateWorkspaceAsync();

        await Assert.ThrowsAsync<LastOwnerProtectionException>(
            () => _service.RemoveMemberAsync(workspaceId, _owner, _owner));
    }

    [Fact]
    public async Task LastOwner_CannotBeDemoted()
    {
        Guid workspaceId = await CreateWorkspaceAsync();

        await Assert.ThrowsAsync<LastOwnerProtectionException>(
            () => _service.UpdateMemberRolesAsync(workspaceId, _owner, [WorkspaceRoles.Admin], _owner));
    }

    [Fact]
    public async Task LastOwner_CannotBeDisabled()
    {
        Guid workspaceId = await CreateWorkspaceAsync();

        await Assert.ThrowsAsync<LastOwnerProtectionException>(
            () => _service.SetMemberStateAsync(workspaceId, _owner, WorkspaceAccessStates.Disabled, _owner));
    }

    [Fact]
    public async Task DisabledOwner_DoesNotCountTowardTheInvariant()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        Guid secondOwner = Guid.NewGuid();
        await _service.AddMemberAsync(workspaceId, secondOwner, [WorkspaceRoles.Owner], _owner);

        await _service.SetMemberStateAsync(workspaceId, secondOwner, WorkspaceAccessStates.Disabled, _owner);

        // The remaining active owner is now the last one.
        await Assert.ThrowsAsync<LastOwnerProtectionException>(
            () => _service.RemoveMemberAsync(workspaceId, _owner, _owner));
    }

    // ── Policy-based permissions ──────────────────────────────────────────────

    [Fact]
    public async Task Member_CannotInviteOrManage()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        Guid member = Guid.NewGuid();
        await _service.AddMemberAsync(workspaceId, member, [WorkspaceRoles.Member], _owner);

        await Assert.ThrowsAsync<WorkspacePermissionException>(
            () => _service.InviteAsync(workspaceId, "x@example.com", "x@example.com", [WorkspaceRoles.Member], member));

        await Assert.ThrowsAsync<WorkspacePermissionException>(
            () => _service.RemoveMemberAsync(workspaceId, _owner, member));
    }

    [Fact]
    public async Task Admin_ManagesMembers_ButNotOwners()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        Guid admin = Guid.NewGuid();
        Guid member = Guid.NewGuid();
        await _service.AddMemberAsync(workspaceId, admin, [WorkspaceRoles.Admin], _owner);

        await _service.AddMemberAsync(workspaceId, member, [WorkspaceRoles.Member], admin);

        // An admin cannot grant Owner, remove an owner, or delete the workspace.
        await Assert.ThrowsAsync<WorkspacePermissionException>(
            () => _service.UpdateMemberRolesAsync(workspaceId, member, [WorkspaceRoles.Owner], admin));

        await Assert.ThrowsAsync<WorkspacePermissionException>(
            () => _service.RemoveMemberAsync(workspaceId, _owner, admin));

        await Assert.ThrowsAsync<WorkspacePermissionException>(
            () => _service.DeleteWorkspaceAsync(workspaceId, admin));
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentRoleChange_LosesOnStaleETag()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        Guid member = Guid.NewGuid();
        await _service.AddMemberAsync(workspaceId, member, [WorkspaceRoles.Member], _owner);

        // Simulate a stale write: capture the document, let someone else write, then replay.
        WorkspaceAccessDocument stale = (await _service.GetMembershipAsync(workspaceId, member))!;

        await _service.UpdateMemberRolesAsync(workspaceId, member, [WorkspaceRoles.Admin], _owner);

        stale.Roles = [WorkspaceRoles.Member];
        await Assert.ThrowsAsync<CoreConcurrencyException>(() => _access.ReplaceAsync(stale));
    }

    // ── Invitations ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Invitation_CarriesPositiveTtl_MembershipCarriesNone()
    {
        Guid workspaceId = await CreateWorkspaceAsync();

        WorkspaceAccessDocument invitation = await _service.InviteAsync(
            workspaceId, "new@example.com", "new@example.com", [WorkspaceRoles.Member], _owner);

        Assert.NotNull(invitation.Ttl);
        Assert.True(invitation.Ttl > 0);
        Assert.NotNull(invitation.ExpiresOn);

        WorkspaceAccessDocument ownerMembership = (await _service.GetMembershipAsync(workspaceId, _owner))!;
        Assert.Null(ownerMembership.Ttl);
        Assert.Null(ownerMembership.ExpiresOn);
    }

    [Fact]
    public async Task Invitation_AcceptedOnce_SecondAcceptFails()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        WorkspaceAccessDocument invitation = await _service.InviteAsync(
            workspaceId, "new@example.com", "new@example.com", [WorkspaceRoles.Member], _owner);

        Guid invitee = Guid.NewGuid();
        WorkspaceAccessDocument membership = await _service.AcceptInvitationAsync(workspaceId, invitation.Id, invitee);
        Assert.Equal([WorkspaceRoles.Member], membership.Roles);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AcceptInvitationAsync(workspaceId, invitation.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Invitation_Expired_CannotBeAccepted()
    {
        _configuration.InvitationLifetime = TimeSpan.FromMilliseconds(-1);
        Guid workspaceId = await CreateWorkspaceAsync();

        WorkspaceAccessDocument invitation = await _service.InviteAsync(
            workspaceId, "new@example.com", "new@example.com", [], _owner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AcceptInvitationAsync(workspaceId, invitation.Id, Guid.NewGuid()));
    }

    // ── Deletion and the index ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteWorkspace_RemovesAccessAndIndexEntries()
    {
        Guid workspaceId = await CreateWorkspaceAsync();
        Guid member = Guid.NewGuid();
        await _service.AddMemberAsync(workspaceId, member, [WorkspaceRoles.Member], _owner);

        Assert.Contains(workspaceId, await _index.GetWorkspaceIdsAsync("default", member));

        await _service.DeleteWorkspaceAsync(workspaceId, _owner);

        Assert.Empty(await _service.GetAccessAsync(workspaceId));
        Assert.Null(await _workspaces.GetAsync(workspaceId));
        Assert.Empty(await _index.GetWorkspaceIdsAsync("default", member));
        Assert.Empty(await _index.GetWorkspaceIdsAsync("default", _owner));
    }
}
