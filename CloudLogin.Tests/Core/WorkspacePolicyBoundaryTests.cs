using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public sealed class WorkspacePolicyBoundaryTests
{
    public static IEnumerable<object[]> InactiveAccess()
    {
        foreach (WorkspaceAccessStates state in Enum.GetValues<WorkspaceAccessStates>())
        foreach (WorkspaceAccessKinds kind in Enum.GetValues<WorkspaceAccessKinds>())
        foreach (string role in new[] { "Owner", "Admin", "Member", "Unrecognized" })
            if (kind != WorkspaceAccessKinds.Membership || state != WorkspaceAccessStates.Active)
                yield return [kind, state, role];
    }

    [Theory]
    [MemberData(nameof(InactiveAccess))]
    public void InactiveMembershipsAndInvitationsNeverGrantManagementRights(WorkspaceAccessKinds kind, WorkspaceAccessStates state, string role)
    {
        WorkspaceAccessDocument access = new() { Kind = kind, State = state, Roles = [role] };
        Assert.False(WorkspaceRolePolicy.IsActiveMember(access));
        Assert.False(WorkspaceRolePolicy.IsActiveOwner(access));
        Assert.False(WorkspaceRolePolicy.IsActiveAdmin(access));
        Assert.False(WorkspaceRolePolicy.CanManageMembers(access));
        Assert.False(WorkspaceRolePolicy.CanEditProfile(access));
        Assert.False(WorkspaceRolePolicy.CanDeleteWorkspace(access));
        Assert.False(WorkspaceRolePolicy.CanManageOwners(access));
    }

    [Theory]
    [InlineData("Owner", true, true)]
    [InlineData("owner", true, true)]
    [InlineData("OWNER", true, true)]
    [InlineData("Admin", true, false)]
    [InlineData("ADMIN", true, false)]
    [InlineData("Member", false, false)]
    [InlineData("GlobalAdmin", false, false)]
    [InlineData("", false, false)]
    [InlineData(" Owner ", false, false)]
    public void ActiveRolesGrantOnlyTheirDeclaredCapabilities(string role, bool manageMembers, bool manageOwners)
    {
        WorkspaceAccessDocument access = new() { Kind = WorkspaceAccessKinds.Membership, State = WorkspaceAccessStates.Active, Roles = [role] };
        Assert.True(WorkspaceRolePolicy.IsActiveMember(access));
        Assert.Equal(manageMembers, WorkspaceRolePolicy.CanManageMembers(access));
        Assert.Equal(manageMembers, WorkspaceRolePolicy.CanEditProfile(access));
        Assert.Equal(manageOwners, WorkspaceRolePolicy.CanManageOwners(access));
        Assert.Equal(manageOwners, WorkspaceRolePolicy.CanDeleteWorkspace(access));
    }

    [Fact]
    public void RemovingPrivilegedRoleImmediatelyChangesPolicy()
    {
        WorkspaceAccessDocument access = new() { Kind = WorkspaceAccessKinds.Membership, State = WorkspaceAccessStates.Active, Roles = ["Member", "Owner"] };
        Assert.True(WorkspaceRolePolicy.CanDeleteWorkspace(access));
        access.Roles = ["Member"];
        Assert.False(WorkspaceRolePolicy.CanDeleteWorkspace(access));
        Assert.False(WorkspaceRolePolicy.CanManageMembers(access));
    }
}
