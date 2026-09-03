using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.API.V3;

[Route("api/v3/workspaces")]
[Authorize]
public sealed class V3WorkspacesController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    [HttpGet]
    public async Task<ActionResult<List<V3WorkspaceResponse>>> GetMine()
    {
        (WorkspaceAccessService? workspaces, IWorkspaceAccessRepository? accessRepository, Guid? userId) = await ResolveAsync();
        if (workspaces is null || accessRepository is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        IWorkspaceRepository workspaceRepository = HttpContext.RequestServices.GetRequiredService<IWorkspaceRepository>();
        List<WorkspaceAccessDocument> memberships = await accessRepository.GetMembershipsForUserAsync(userId.Value);

        List<V3WorkspaceResponse> responses = [];

        foreach (WorkspaceAccessDocument membership in memberships.Where(WorkspaceRolePolicy.IsActiveMember))
        {
            if (!Guid.TryParse(membership.WorkspaceId, out Guid workspaceId))
                continue;

            WorkspaceDocument? workspace = await workspaceRepository.GetAsync(workspaceId);
            if (workspace is null || workspace.State != WorkspaceStates.Active)
                continue;

            responses.Add(ToResponse(workspace, membership));
        }

        return Ok(responses);
    }

    [HttpPost]
    public async Task<ActionResult<V3WorkspaceResponse>> Create([FromBody] V3CreateWorkspaceRequest request)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest();

        WorkspaceDocument workspace = await workspaces.CreateWorkspaceAsync(userId.Value, request.Name.Trim());
        WorkspaceAccessDocument membership = (await workspaces.GetMembershipAsync(Guid.Parse(workspace.Id), userId.Value))!;

        return Ok(ToResponse(workspace, membership));
    }

    [HttpGet("{workspaceId:guid}/members")]
    public async Task<ActionResult<List<V3WorkspaceMemberResponse>>> GetMembers(Guid workspaceId)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        WorkspaceAccessDocument? membership = await workspaces.GetMembershipAsync(workspaceId, userId.Value);
        if (membership is null || !WorkspaceRolePolicy.IsActiveMember(membership))
            return NotFound(); // Membership required; an id alone reveals nothing.

        List<WorkspaceAccessDocument> all = await workspaces.GetAccessAsync(workspaceId);
        IUserRepository users = HttpContext.RequestServices.GetRequiredService<IUserRepository>();

        List<V3WorkspaceMemberResponse> members = [];

        foreach (WorkspaceAccessDocument access in all.Where(record => record.Kind == WorkspaceAccessKinds.Membership))
        {
            if (!Guid.TryParse(access.UserId, out Guid memberId))
                continue;

            UserDocument? member = await users.GetAsync(memberId);

            members.Add(new V3WorkspaceMemberResponse
            {
                UserId = memberId,
                DisplayName = member?.DisplayName,
                Roles = [.. access.Roles],
                State = access.State.ToString()
            });
        }

        return Ok(members);
    }

    [HttpPut("{workspaceId:guid}")]
    public async Task<ActionResult<V3WorkspaceResponse>> Update(
        Guid workspaceId, [FromBody] V3UpdateWorkspaceRequest request)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        IWorkspaceRepository repository =
            HttpContext.RequestServices.GetRequiredService<IWorkspaceRepository>();
        WorkspaceDocument? current = await repository.GetAsync(workspaceId);
        if (current is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name))
            current.Name = request.Name.Trim();
        current.Website = request.Website;

        try
        {
            WorkspaceDocument updated =
                await workspaces.UpdateWorkspaceAsync(workspaceId, current, userId.Value);
            WorkspaceAccessDocument membership =
                (await workspaces.GetMembershipAsync(workspaceId, userId.Value))!;
            return Ok(ToResponse(updated, membership));
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
    }

    [HttpPost("{workspaceId:guid}/invitations")]
    public async Task<ActionResult<V3WorkspaceInvitationResponse>> Invite(Guid workspaceId, [FromBody] V3InviteMemberRequest request)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Recipient))
            return BadRequest();

        IdentityNormalization normalization = HttpContext.RequestServices.GetRequiredService<IdentityNormalization>();
        string recipientKey = request.Recipient.Contains('@')
            ? IdentityNormalization.NormalizeEmail(request.Recipient)
            : normalization.NormalizePhone(request.Recipient);

        try
        {
            WorkspaceAccessDocument invitation = await workspaces.InviteAsync(
                workspaceId, recipientKey, request.Recipient.Trim(), request.Roles, userId.Value);

            return Ok(new V3WorkspaceInvitationResponse
            {
                InvitationId = invitation.Id,
                Recipient = invitation.RecipientDisplay ?? request.Recipient,
                Roles = [.. invitation.Roles],
                ExpiresOn = invitation.ExpiresOn!.Value
            });
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
    }

    [HttpPost("{workspaceId:guid}/invitations/{invitationId}/accept")]
    public async Task<ActionResult<V3WorkspaceMemberResponse>> AcceptInvitation(
        Guid workspaceId, string invitationId)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        try
        {
            WorkspaceAccessDocument membership = await workspaces.AcceptInvitationAsync(
                workspaceId, invitationId, userId.Value);
            return Ok(new V3WorkspaceMemberResponse
            {
                UserId = userId.Value,
                Roles = [.. membership.Roles],
                State = membership.State.ToString()
            });
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Invitation unavailable",
                Detail = exception.Message
            });
        }
    }

    [HttpPut("{workspaceId:guid}/members/{memberId:guid}/roles")]
    public async Task<ActionResult> UpdateRoles(Guid workspaceId, Guid memberId, [FromBody] V3UpdateMemberRolesRequest request)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        try
        {
            await workspaces.UpdateMemberRolesAsync(workspaceId, memberId, request.Roles, userId.Value);
            return NoContent();
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
        catch (LastOwnerProtectionException exception)
        {
            return Conflict(new ProblemDetails { Title = "Last owner protection", Detail = exception.Message });
        }
        catch (CoreConcurrencyException)
        {
            return Conflict(new ProblemDetails { Title = "Concurrent change", Detail = "The membership changed; reload and retry." });
        }
    }

    [HttpDelete("{workspaceId:guid}/members/{memberId:guid}")]
    public async Task<ActionResult> RemoveMember(Guid workspaceId, Guid memberId)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        try
        {
            await workspaces.RemoveMemberAsync(workspaceId, memberId, userId.Value);
            return NoContent();
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
        catch (LastOwnerProtectionException exception)
        {
            return Conflict(new ProblemDetails { Title = "Last owner protection", Detail = exception.Message });
        }
    }

    [HttpPut("{workspaceId:guid}/members/{memberId:guid}/state")]
    public async Task<ActionResult> SetMemberState(
        Guid workspaceId, Guid memberId, [FromBody] V3SetMemberStateRequest request)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();
        if (!Enum.TryParse(request.State, true, out WorkspaceAccessStates state) ||
            state is not (WorkspaceAccessStates.Active or WorkspaceAccessStates.Disabled))
            return BadRequest();

        try
        {
            await workspaces.SetMemberStateAsync(workspaceId, memberId, state, userId.Value);
            return NoContent();
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
        catch (LastOwnerProtectionException exception)
        {
            return Conflict(new ProblemDetails { Title = "Last owner protection", Detail = exception.Message });
        }
    }

    [HttpDelete("{workspaceId:guid}")]
    public async Task<ActionResult> Delete(Guid workspaceId)
    {
        (WorkspaceAccessService? workspaces, _, Guid? userId) = await ResolveAsync();
        if (workspaces is null)
            return CoreUnavailable();
        if (userId is null)
            return Unauthorized();

        try
        {
            await workspaces.DeleteWorkspaceAsync(workspaceId, userId.Value);
            return NoContent();
        }
        catch (WorkspacePermissionException)
        {
            return Forbid();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(WorkspaceAccessService? Workspaces, IWorkspaceAccessRepository? Access, Guid? UserId)> ResolveAsync()
    {
        WorkspaceAccessService? workspaces = CoreService<WorkspaceAccessService>();
        IWorkspaceAccessRepository? accessRepository = CoreService<IWorkspaceAccessRepository>();
        CloudUser? user = await CurrentUserAsync();

        return (workspaces, accessRepository, user?.Id);
    }

    private static V3WorkspaceResponse ToResponse(WorkspaceDocument workspace, WorkspaceAccessDocument membership) => new()
    {
        WorkspaceId = Guid.Parse(workspace.Id),
        Name = workspace.Name,
        Website = workspace.Website,
        CreatedOn = workspace.CreatedOn,
        MyRoles = [.. membership.Roles]
    };
}
