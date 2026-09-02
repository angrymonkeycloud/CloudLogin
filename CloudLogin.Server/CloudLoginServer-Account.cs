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

    /// <summary>
    /// Updates a workspace's profile fields on behalf of a trusted backend caller rather than a
    /// signed-in end-user - <see cref="UpdateWorkspace"/> requires <see cref="CurrentUser"/>, which
    /// a service-to-service request (e.g. CDM's ServiceKey-authenticated field sync) never carries.
    /// The workspace's own recorded owner stands in as the audit actor: not a synthetic "system"
    /// id, because the caller's ServiceKey credential is itself the trust boundary this bypasses,
    /// and <see cref="ICloudLoginWorkspaceRegistry.UpdateAsync"/> only re-checks that the actor
    /// still owns (or manages) the workspace being updated - which its own current owner trivially
    /// satisfies.
    /// </summary>
    public async Task<CloudWorkspace> UpdateWorkspaceAsService(CloudWorkspace workspace)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        return await _workspaceRegistry.UpdateAsync(workspace, workspace.OwnerUserId);
    }

    /// <summary>
    /// Creates a workspace on behalf of a trusted backend caller - the counterpart of a record that
    /// was born in another system (a CDM Business) rather than on the account page. There is no
    /// signed-in user to own it, so the caller names the owner: a workspace without one would be
    /// reachable from nobody's account. The owner's workspace allowance is enforced exactly as it
    /// is for a self-service creation.
    /// </summary>
    public async Task<CloudWorkspace> CreateWorkspaceAsService(string name, Guid ownerUserId)
    {
        if (_workspaceRegistry == null)
            throw new InvalidOperationException("The account registry is not configured on this host.");

        return await _workspaceRegistry.CreateAsync(name, ownerUserId);
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
    /// the caller's standing, and the members list. Returns null when the caller isn't a member,
    /// so a guessed identifier can't confirm a workspace exists. Commercial state (subscriptions,
    /// orders, payments) lives in the owning applications, never here.
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

    /// <summary>
    /// Throws unless the user owns the workspace or holds its Admin role. Every write that
    /// targets a workspace &mdash; invitations, profile updates, deletion &mdash; passes
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
