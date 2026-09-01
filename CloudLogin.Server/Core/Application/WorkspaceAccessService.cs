using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>The operation would leave the workspace without any active owner.</summary>
public sealed class LastOwnerProtectionException() :
    InvalidOperationException("A workspace must keep at least one active owner.");

/// <summary>The caller lacks the role required for the operation.</summary>
public sealed class WorkspacePermissionException(string message) : UnauthorizedAccessException(message);

/// <summary>
/// Policy evaluation for workspace roles. Permissions are decided here, in one place, instead of
/// scattered role-string comparisons.
/// </summary>
public static class WorkspaceRolePolicy
{
    public static bool IsActiveMember(WorkspaceAccessDocument access) =>
        access.Kind == WorkspaceAccessKinds.Membership && access.State == WorkspaceAccessStates.Active;

    public static bool IsActiveOwner(WorkspaceAccessDocument access) =>
        IsActiveMember(access) && access.Roles.Contains(WorkspaceRoles.Owner, StringComparer.OrdinalIgnoreCase);

    public static bool IsActiveAdmin(WorkspaceAccessDocument access) =>
        IsActiveMember(access) &&
        (access.Roles.Contains(WorkspaceRoles.Owner, StringComparer.OrdinalIgnoreCase) ||
         access.Roles.Contains(WorkspaceRoles.Admin, StringComparer.OrdinalIgnoreCase));

    /// <summary>Owners manage everything; admins manage members and profile; members read.</summary>
    public static bool CanManageMembers(WorkspaceAccessDocument access) => IsActiveAdmin(access);
    public static bool CanEditProfile(WorkspaceAccessDocument access) => IsActiveAdmin(access);
    public static bool CanDeleteWorkspace(WorkspaceAccessDocument access) => IsActiveOwner(access);
    public static bool CanManageOwners(WorkspaceAccessDocument access) => IsActiveOwner(access);
}

/// <summary>
/// Workspace membership over the <c>Workspaces</c> and <c>WorkspaceAccess</c> containers:
/// multiple owners, policy-based permissions, ETag-guarded concurrent changes, invitations that
/// expire through Cosmos TTL, and a hard invariant that the last active owner can never leave,
/// be removed, be disabled, or be demoted.
/// </summary>
public sealed class WorkspaceAccessService(
    IWorkspaceRepository workspaces,
    IWorkspaceAccessRepository access,
    IUserWorkspaceIndexStore? userWorkspaceIndex,
    CloudLoginCoreConfiguration configuration,
    IAuditLogger audit,
    IUserRepository? users = null)
{
    private readonly IWorkspaceRepository _workspaces = workspaces;
    private readonly IWorkspaceAccessRepository _access = access;
    private readonly IUserWorkspaceIndexStore? _index = userWorkspaceIndex;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly IAuditLogger _audit = audit;
    private readonly IUserRepository? _users = users;

    // ── Workspace lifecycle ───────────────────────────────────────────────────

    public async Task<WorkspaceDocument> CreateWorkspaceAsync(Guid ownerUserId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid workspaceId = Guid.NewGuid();

        WorkspaceDocument workspace = new()
        {
            Id = workspaceId.ToString(),
            Name = name.Trim(),
            CreatedOn = now,
            UpdatedOn = now
        };

        WorkspaceAccessDocument ownerMembership = new()
        {
            Id = WorkspaceAccessDocument.MembershipId(ownerUserId),
            WorkspaceId = workspace.Id,
            Kind = WorkspaceAccessKinds.Membership,
            State = WorkspaceAccessStates.Active,
            UserId = ownerUserId.ToString(),
            Roles = [WorkspaceRoles.Owner],
            CreatedOn = now,
            UpdatedOn = now
        };

        await _workspaces.CreateAsync(workspace, cancellationToken);
        try
        {
            await _access.CreateAsync(ownerMembership, cancellationToken);
        }
        catch
        {
            await _workspaces.DeleteAsync(workspaceId, cancellationToken);
            throw;
        }
        await UpdateIndexSafeAsync(ownerUserId, workspaceId, remove: false, cancellationToken);
        await _audit.LogAsync("Workspace.Created", ownerUserId,
            data: new Dictionary<string, string> { ["WorkspaceId"] = workspace.Id }, cancellationToken: cancellationToken);

        return workspace;
    }

    public async Task DeleteWorkspaceAsync(Guid workspaceId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument actor = await RequireMembershipAsync(workspaceId, actingUserId, cancellationToken);
        if (!WorkspaceRolePolicy.CanDeleteWorkspace(actor))
            throw new WorkspacePermissionException("Only an owner can delete the workspace.");

        List<WorkspaceAccessDocument> allAccess = await _access.GetAllForWorkspaceAsync(workspaceId, cancellationToken);

        await _access.DeleteAllForWorkspaceAsync(workspaceId, cancellationToken);
        await _workspaces.DeleteAsync(workspaceId, cancellationToken);

        foreach (WorkspaceAccessDocument member in allAccess.Where(candidate => candidate.Kind == WorkspaceAccessKinds.Membership))
            if (Guid.TryParse(member.UserId, out Guid memberId))
                await UpdateIndexSafeAsync(memberId, workspaceId, remove: true, cancellationToken);

        await _audit.LogAsync("Workspace.Deleted", actingUserId,
            data: new Dictionary<string, string> { ["WorkspaceId"] = workspaceId.ToString() }, cancellationToken: cancellationToken);
    }

    public async Task<WorkspaceDocument> UpdateWorkspaceAsync(
        Guid workspaceId, WorkspaceDocument changes, Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument actor =
            await RequireMembershipAsync(workspaceId, actingUserId, cancellationToken);
        if (!WorkspaceRolePolicy.CanEditProfile(actor))
            throw new WorkspacePermissionException("Not allowed to edit the workspace profile.");

        WorkspaceDocument workspace = await _workspaces.GetAsync(workspaceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");

        ArgumentException.ThrowIfNullOrWhiteSpace(changes.Name);
        workspace.Name = changes.Name.Trim();
        workspace.LegalName = Clean(changes.LegalName);
        workspace.Website = Clean(changes.Website);
        workspace.TaxId = Clean(changes.TaxId);
        workspace.BillingContactName = Clean(changes.BillingContactName);
        workspace.BillingContactEmail = Clean(changes.BillingContactEmail);
        workspace.BillingContactPhone = Clean(changes.BillingContactPhone);
        workspace.UpdatedOn = DateTimeOffset.UtcNow;
        await _workspaces.ReplaceAsync(workspace, cancellationToken);
        return workspace;
    }

    // ── Membership ────────────────────────────────────────────────────────────

    public async Task<WorkspaceAccessDocument> AddMemberAsync(
        Guid workspaceId, Guid userId, IReadOnlyList<string> roles, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument actor = await RequireMembershipAsync(workspaceId, actingUserId, cancellationToken);

        bool grantsOwner = roles.Contains(WorkspaceRoles.Owner, StringComparer.OrdinalIgnoreCase);
        if (grantsOwner ? !WorkspaceRolePolicy.CanManageOwners(actor) : !WorkspaceRolePolicy.CanManageMembers(actor))
            throw new WorkspacePermissionException("Not allowed to add members with these roles.");

        DateTimeOffset now = DateTimeOffset.UtcNow;

        WorkspaceAccessDocument membership = new()
        {
            Id = WorkspaceAccessDocument.MembershipId(userId),
            WorkspaceId = workspaceId.ToString(),
            Kind = WorkspaceAccessKinds.Membership,
            State = WorkspaceAccessStates.Active,
            UserId = userId.ToString(),
            Roles = [.. roles],
            CreatedOn = now,
            UpdatedOn = now
        };

        await _access.CreateAsync(membership, cancellationToken);
        await UpdateIndexSafeAsync(userId, workspaceId, remove: false, cancellationToken);

        return membership;
    }

    /// <summary>
    /// Replaces a member's roles under the member document's ETag. Demoting an owner requires
    /// owner permission and another active owner to remain.
    /// </summary>
    public async Task UpdateMemberRolesAsync(
        Guid workspaceId, Guid userId, IReadOnlyList<string> roles, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument actor = await RequireMembershipAsync(workspaceId, actingUserId, cancellationToken);
        WorkspaceAccessDocument target = await RequireMembershipAsync(workspaceId, userId, cancellationToken);

        bool targetIsOwner = WorkspaceRolePolicy.IsActiveOwner(target);
        bool grantsOwner = roles.Contains(WorkspaceRoles.Owner, StringComparer.OrdinalIgnoreCase);

        if ((targetIsOwner || grantsOwner) ? !WorkspaceRolePolicy.CanManageOwners(actor) : !WorkspaceRolePolicy.CanManageMembers(actor))
            throw new WorkspacePermissionException("Not allowed to change these roles.");

        List<WorkspaceAccessDocument>? ownerGuard = targetIsOwner && !grantsOwner
            ? await GetOwnerGuardAsync(workspaceId, userId, cancellationToken)
            : null;

        target.Roles = [.. roles];
        target.UpdatedOn = DateTimeOffset.UtcNow;
        if (ownerGuard is null)
            await _access.ReplaceAsync(target, cancellationToken);
        else
            await _access.ReplaceWithOwnerGuardAsync(target, ownerGuard, cancellationToken);
    }

    /// <summary>Removes (or lets leave) a member. The final active owner can do neither.</summary>
    public async Task RemoveMemberAsync(Guid workspaceId, Guid userId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        bool leavingSelf = userId == actingUserId;

        WorkspaceAccessDocument actor = await RequireMembershipAsync(workspaceId, actingUserId, cancellationToken);
        WorkspaceAccessDocument target = await RequireMembershipAsync(workspaceId, userId, cancellationToken);

        if (!leavingSelf &&
            (WorkspaceRolePolicy.IsActiveOwner(target) ? !WorkspaceRolePolicy.CanManageOwners(actor) : !WorkspaceRolePolicy.CanManageMembers(actor)))
            throw new WorkspacePermissionException("Not allowed to remove this member.");

        List<WorkspaceAccessDocument>? ownerGuard = WorkspaceRolePolicy.IsActiveOwner(target)
            ? await GetOwnerGuardAsync(workspaceId, userId, cancellationToken)
            : null;

        if (ownerGuard is null)
            await _access.DeleteAsync(workspaceId, target.Id, cancellationToken);
        else
            await _access.DeleteWithOwnerGuardAsync(target, ownerGuard, cancellationToken);
        await UpdateIndexSafeAsync(userId, workspaceId, remove: true, cancellationToken);
    }

    /// <summary>Disables or re-enables a membership. Disabling the final active owner is refused.</summary>
    public async Task SetMemberStateAsync(
        Guid workspaceId, Guid userId, WorkspaceAccessStates state, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument actor = await RequireMembershipAsync(workspaceId, actingUserId, cancellationToken);
        WorkspaceAccessDocument target = await RequireMembershipAsync(workspaceId, userId, cancellationToken);

        if (WorkspaceRolePolicy.IsActiveOwner(target) ? !WorkspaceRolePolicy.CanManageOwners(actor) : !WorkspaceRolePolicy.CanManageMembers(actor))
            throw new WorkspacePermissionException("Not allowed to change this member's state.");

        List<WorkspaceAccessDocument>? ownerGuard =
            WorkspaceRolePolicy.IsActiveOwner(target) && state != WorkspaceAccessStates.Active
                ? await GetOwnerGuardAsync(workspaceId, userId, cancellationToken)
                : null;

        target.State = state;
        target.UpdatedOn = DateTimeOffset.UtcNow;
        if (ownerGuard is null)
            await _access.ReplaceAsync(target, cancellationToken);
        else
            await _access.ReplaceWithOwnerGuardAsync(target, ownerGuard, cancellationToken);
    }

    // ── Invitations ───────────────────────────────────────────────────────────

    public async Task<WorkspaceAccessDocument> InviteAsync(
        Guid workspaceId, string recipientKey, string recipientDisplay, IReadOnlyList<string> roles,
        Guid invitedByUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument actor = await RequireMembershipAsync(workspaceId, invitedByUserId, cancellationToken);
        bool invitesOwner = roles.Contains(WorkspaceRoles.Owner, StringComparer.OrdinalIgnoreCase);
        if (invitesOwner ? !WorkspaceRolePolicy.CanManageOwners(actor) : !WorkspaceRolePolicy.CanManageMembers(actor))
            throw new WorkspacePermissionException("Not allowed to invite members.");

        DateTimeOffset now = DateTimeOffset.UtcNow;

        WorkspaceAccessDocument invitation = new()
        {
            Id = WorkspaceAccessDocument.InvitationId(Guid.NewGuid()),
            WorkspaceId = workspaceId.ToString(),
            Kind = WorkspaceAccessKinds.Invitation,
            State = WorkspaceAccessStates.Pending,
            RecipientKey = recipientKey,
            RecipientDisplay = recipientDisplay,
            Roles = [.. roles],
            InvitedByUserId = invitedByUserId.ToString(),
            CreatedOn = now,
            UpdatedOn = now,
            ExpiresOn = now + _configuration.InvitationLifetime
        };

        DocumentExpiry.Recompute(invitation, now);
        await _access.CreateAsync(invitation, cancellationToken);

        return invitation;
    }

    /// <summary>Accepts an invitation: the invitation converts to a membership exactly once.</summary>
    public async Task<WorkspaceAccessDocument> AcceptInvitationAsync(
        Guid workspaceId, string invitationId, Guid acceptingUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument? invitation = await _access.GetAsync(workspaceId, invitationId, cancellationToken);

        if (invitation is null || invitation.Kind != WorkspaceAccessKinds.Invitation ||
            invitation.State != WorkspaceAccessStates.Pending || DocumentExpiry.IsExpired(invitation))
            throw new InvalidOperationException("The invitation is no longer valid.");

        if (_users is not null)
        {
            UserDocument? user = await _users.GetAsync(acceptingUserId, cancellationToken);
            bool ownsRecipient = user?.Contacts.Any(contact =>
                contact.IsVerified &&
                string.Equals(contact.NormalizedValue, invitation.RecipientKey, StringComparison.Ordinal)) == true;
            if (!ownsRecipient)
                throw new WorkspacePermissionException(
                    "The invitation recipient does not belong to the signed-in user.");
        }

        invitation.State = WorkspaceAccessStates.Revoked; // consumed; TTL removes it
        invitation.UpdatedOn = DateTimeOffset.UtcNow;
        DocumentExpiry.Recompute(invitation);
        WorkspaceAccessDocument membership = new()
        {
            Id = WorkspaceAccessDocument.MembershipId(acceptingUserId),
            WorkspaceId = workspaceId.ToString(),
            Kind = WorkspaceAccessKinds.Membership,
            State = WorkspaceAccessStates.Active,
            UserId = acceptingUserId.ToString(),
            Roles = invitation.Roles.Count > 0 ? [.. invitation.Roles] : [WorkspaceRoles.Member],
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        };

        await _access.AcceptInvitationAsync(invitation, membership, cancellationToken);
        await UpdateIndexSafeAsync(acceptingUserId, workspaceId, remove: false, cancellationToken);

        return membership;
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    public Task<List<WorkspaceAccessDocument>> GetAccessAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        _access.GetAllForWorkspaceAsync(workspaceId, cancellationToken);

    public async Task<WorkspaceAccessDocument?> GetMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) =>
        await _access.GetAsync(workspaceId, WorkspaceAccessDocument.MembershipId(userId), cancellationToken);

    /// <summary>Active owner count — the invariant every mutation protects.</summary>
    public async Task<int> CountActiveOwnersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        List<WorkspaceAccessDocument> all = await _access.GetAllForWorkspaceAsync(workspaceId, cancellationToken);
        return all.Count(WorkspaceRolePolicy.IsActiveOwner);
    }

    // ── Invariant enforcement ─────────────────────────────────────────────────

    private async Task<List<WorkspaceAccessDocument>> GetOwnerGuardAsync(
        Guid workspaceId, Guid excludingUserId, CancellationToken cancellationToken)
    {
        List<WorkspaceAccessDocument> all = await _access.GetAllForWorkspaceAsync(workspaceId, cancellationToken);
        List<WorkspaceAccessDocument> owners = [.. all.Where(WorkspaceRolePolicy.IsActiveOwner)];
        if (!owners.Any(candidate =>
                !string.Equals(candidate.UserId, excludingUserId.ToString(), StringComparison.OrdinalIgnoreCase)))
            throw new LastOwnerProtectionException();
        if (owners.Count > 99)
            throw new InvalidOperationException(
                "A workspace with more than 99 active owners requires owner-count reconciliation before mutation.");
        return owners;
    }

    private async Task<WorkspaceAccessDocument> RequireMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await GetMembershipAsync(workspaceId, userId, cancellationToken)
        ?? throw new WorkspacePermissionException("Not a member of this workspace.");

    private async Task UpdateIndexSafeAsync(Guid userId, Guid workspaceId, bool remove, CancellationToken cancellationToken)
    {
        if (_index is null)
            return;

        // Non-authoritative index: never let its failure fail the operation; reconciliation repairs it.
        try
        {
            if (remove)
                await _index.DeleteAsync(_configuration.RealmId, userId, workspaceId, cancellationToken);
            else
                await _index.UpsertAsync(_configuration.RealmId, userId, workspaceId, cancellationToken);
        }
        catch
        {
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
