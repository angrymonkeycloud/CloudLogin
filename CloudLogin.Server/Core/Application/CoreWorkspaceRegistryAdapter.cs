using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>Workspace registry contract projected over the shared multi-owner core.</summary>
public sealed class CoreWorkspaceRegistryAdapter(
    WorkspaceAccessService service,
    IWorkspaceRepository workspaces,
    IWorkspaceAccessRepository access,
    IdentityNormalization normalization,
    CloudLoginWebConfiguration configuration) : ICloudLoginWorkspaceRegistry
{
    private WorkspaceConfiguration Options => configuration.Workspace ?? new WorkspaceConfiguration();

    public async Task<CloudWorkspace> CreateAsync(
        string name, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        CloudWorkspaceQuota quota = await GetQuotaAsync(ownerUserId, cancellationToken);
        if (!quota.CanCreate)
            throw new CloudWorkspaceLimitReachedException(
                quota.RemainingOwned <= 0 ? CloudWorkspaceLimitKinds.Owned : CloudWorkspaceLimitKinds.Membership,
                quota.RemainingOwned <= 0 ? quota.MaxOwned : quota.MaxTotal,
                Options.SingularLabel, Options.PluralLabel);

        WorkspaceDocument workspace =
            await service.CreateWorkspaceAsync(ownerUserId, name, cancellationToken);
        return await ToLegacyAsync(workspace, cancellationToken);
    }

    public async Task<CloudWorkspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        WorkspaceDocument? workspace = await workspaces.GetAsync(workspaceId, cancellationToken);
        return workspace is null || workspace.State == WorkspaceStates.Deleted
            ? null
            : await ToLegacyAsync(workspace, cancellationToken);
    }

    public async Task<IReadOnlyList<CloudWorkspace>> GetWorkspacesForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        List<CloudWorkspace> result = [];
        foreach (WorkspaceAccessDocument membership in
                 await access.GetMembershipsForUserAsync(userId, cancellationToken))
        {
            if (!WorkspaceRolePolicy.IsActiveMember(membership) ||
                !Guid.TryParse(membership.WorkspaceId, out Guid workspaceId))
                continue;

            CloudWorkspace? workspace = await GetAsync(workspaceId, cancellationToken);
            if (workspace is not null)
                result.Add(workspace);
        }
        return result;
    }

    public async Task<IReadOnlyList<CloudWorkspace>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<CloudWorkspace> result = [];
        foreach (WorkspaceDocument workspace in await workspaces.GetAllAsync(cancellationToken))
            if (workspace.State != WorkspaceStates.Deleted)
                result.Add(await ToLegacyAsync(workspace, cancellationToken));
        return result;
    }

    public async Task<IReadOnlyList<CloudWorkspaceMember>> GetMembersAsync(
        Guid workspaceId, CancellationToken cancellationToken = default) =>
        [.. (await access.GetAllForWorkspaceAsync(workspaceId, cancellationToken))
            .Where(item => item.Kind == WorkspaceAccessKinds.Membership)
            .Select(ToLegacy)];

    public async Task<CloudWorkspaceMember> AddMemberAsync(
        Guid workspaceId, Guid userId, IReadOnlyList<string>? roles = null,
        CancellationToken cancellationToken = default)
    {
        CloudWorkspaceQuota quota = await GetQuotaAsync(userId, cancellationToken);
        if (!quota.CanJoin)
            throw new CloudWorkspaceLimitReachedException(
                CloudWorkspaceLimitKinds.Membership, quota.MaxTotal,
                Options.SingularLabel, Options.PluralLabel);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkspaceAccessDocument membership = new()
        {
            Id = WorkspaceAccessDocument.MembershipId(userId),
            WorkspaceId = workspaceId.ToString(),
            Kind = WorkspaceAccessKinds.Membership,
            State = WorkspaceAccessStates.Active,
            UserId = userId.ToString(),
            Roles = roles is { Count: > 0 } ? [.. roles] : [WorkspaceRoles.Member],
            CreatedOn = now,
            UpdatedOn = now
        };
        await access.CreateAsync(membership, cancellationToken);
        return ToLegacy(membership);
    }

    public async Task<CloudWorkspaceInvitation> InviteAsync(
        Guid workspaceId, string recipient, Guid invitedByUserId, DateTimeOffset expiresOn,
        IReadOnlyList<string>? roles = null, CancellationToken cancellationToken = default)
    {
        string recipientKey = recipient.Contains('@')
            ? IdentityNormalization.NormalizeEmail(recipient)
            : normalization.NormalizePhone(recipient);
        WorkspaceAccessDocument invitation = await service.InviteAsync(
            workspaceId, recipientKey, recipient.Trim(), roles ?? [], invitedByUserId, cancellationToken);

        return new CloudWorkspaceInvitation
        {
            Id = ParseInvitationId(invitation.Id),
            WorkspaceId = workspaceId,
            Recipient = invitation.RecipientDisplay ?? recipient,
            Roles = invitation.Roles,
            ExpiresOn = invitation.ExpiresOn ?? expiresOn,
            CreatedOn = invitation.CreatedOn,
            InvitedByUserId = invitedByUserId
        };
    }

    public async Task<CloudWorkspace> UpdateAsync(
        CloudWorkspace workspace, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceDocument current = await workspaces.GetAsync(workspace.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{workspace.Id}' was not found.");
        current.Name = workspace.Name;
        current.LegalName = workspace.LegalName;
        current.AdditionalBillingInformation = workspace.AdditionalBillingInformation;
        current.BillingEmail = workspace.BillingEmail;
        current.BillingPhone = workspace.BillingPhone;
        current.Website = workspace.Website;
        current.TaxNumber = workspace.TaxNumber;
        current.BillingAddress = ToDocument(workspace.BillingAddress);
        WorkspaceDocument updated = await service.UpdateWorkspaceAsync(
            workspace.Id, current, callerUserId, cancellationToken);
        return await ToLegacyAsync(updated, cancellationToken);
    }

    public async Task<CloudWorkspaceQuota> GetQuotaAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CloudWorkspace> mine =
            await GetWorkspacesForUserAsync(userId, cancellationToken);
        IReadOnlyList<CloudWorkspaceMember> owned = [];
        int ownerCount = 0;
        foreach (CloudWorkspace workspace in mine)
        {
            owned = await GetMembersAsync(workspace.Id, cancellationToken);
            if (owned.Any(member => member.UserId == userId && member.IsOwner))
                ownerCount++;
        }

        return new CloudWorkspaceQuota
        {
            Owned = ownerCount,
            MaxOwned = Options.EffectiveMaxOwnedPerUser,
            Total = mine.Count,
            MaxTotal = Options.EffectiveMaxPerUser
        };
    }

    public async Task<CloudWorkspaceDeletionReport> GetDeletionReportAsync(
        Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        WorkspaceAccessDocument membership =
            await service.GetMembershipAsync(workspaceId, callerUserId, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (!WorkspaceRolePolicy.IsActiveOwner(membership))
            throw new UnauthorizedAccessException("Only an owner may delete this workspace.");

        IReadOnlyList<CloudWorkspaceMember> members =
            await GetMembersAsync(workspaceId, cancellationToken);
        return new CloudWorkspaceDeletionReport
        {
            WorkspaceId = workspaceId,
            Blockers = CloudWorkspaceDeletionBlockers.None,
            OtherMemberCount = members.Count(member => member.UserId != callerUserId),
            Reasons = []
        };
    }

    public Task DeleteAsync(
        Guid workspaceId, Guid callerUserId, CancellationToken cancellationToken = default) =>
        service.DeleteWorkspaceAsync(workspaceId, callerUserId, cancellationToken);

    private async Task<CloudWorkspace> ToLegacyAsync(
        WorkspaceDocument workspace, CancellationToken cancellationToken)
    {
        WorkspaceAccessDocument? primaryOwner =
            (await access.GetAllForWorkspaceAsync(Guid.Parse(workspace.Id), cancellationToken))
            .Where(WorkspaceRolePolicy.IsActiveOwner)
            .OrderBy(item => item.CreatedOn)
            .ThenBy(item => item.UserId, StringComparer.Ordinal)
            .FirstOrDefault();

        return new CloudWorkspace
        {
            Id = Guid.Parse(workspace.Id),
            Name = workspace.Name,
            OwnerUserId = Guid.TryParse(primaryOwner?.UserId, out Guid ownerId) ? ownerId : Guid.Empty,
            CreatedOn = workspace.CreatedOn,
            UpdatedOn = workspace.UpdatedOn,
            LegalName = workspace.LegalName,
            AdditionalBillingInformation = workspace.AdditionalBillingInformation,
            BillingEmail = workspace.BillingEmail,
            BillingPhone = workspace.BillingPhone,
            Website = workspace.Website,
            TaxNumber = workspace.TaxNumber,
            BillingAddress = ToContract(workspace.BillingAddress)
        };
    }

    private static CloudWorkspaceAddress ToContract(WorkspaceAddress? address) => new()
    {
        Line1 = address?.Line1,
        Line2 = address?.Line2,
        City = address?.City,
        State = address?.State,
        PostalCode = address?.PostalCode,
        Country = address?.Country
    };

    private static WorkspaceAddress ToDocument(CloudWorkspaceAddress? address) => new()
    {
        Line1 = address?.Line1,
        Line2 = address?.Line2,
        City = address?.City,
        State = address?.State,
        PostalCode = address?.PostalCode,
        Country = address?.Country
    };

    private static CloudWorkspaceMember ToLegacy(WorkspaceAccessDocument membership) => new()
    {
        WorkspaceId = Guid.Parse(membership.WorkspaceId),
        UserId = Guid.Parse(membership.UserId!),
        State = Enum.TryParse(membership.State.ToString(), out CloudWorkspaceMembershipStates state)
            ? state : CloudWorkspaceMembershipStates.Active,
        Roles = membership.Roles,
        IsOwner = WorkspaceRolePolicy.IsActiveOwner(membership),
        JoinedOn = membership.CreatedOn
    };

    private static Guid ParseInvitationId(string id) =>
        Guid.TryParse(id.Split('|').LastOrDefault(), out Guid value) ? value : Guid.Empty;
}
