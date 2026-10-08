using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.V3.Admin;

[Route("api/v3/admin")]
public sealed class V3AdminController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet("me")]
    public Task<IActionResult> Me() => AdminAsync(null, async access =>
    {
        CloudUser? user = await CurrentUserAsync();

        return Ok(new V3AdminMeResponse
        {
            UserId = access.UserId,
            DisplayName = user?.DisplayName,
            IsGlobalAdmin = access.IsGlobalAdmin,
            Roles = [.. access.Roles.Select(role => role.ToString())],
            Permissions = [.. access.Permissions.Order(StringComparer.Ordinal)]
        });
    });

    [HttpGet("dashboard")]
    public Task<IActionResult> Dashboard() => AdminAsync(AdminPermissions.DashboardRead, async _ =>
    {
        DashboardSummary summary = await Service<AdminDashboardService>().GetAsync(Cancellation);

        return Ok(new V3AdminDashboardResponse
        {
            Users = summary.Users,
            ActiveSessions = summary.ActiveSessions,
            Applications = summary.Applications,
            ActiveApplications = summary.ActiveApplications,
            BlockedApplications = summary.BlockedApplications,
            DormantApplications = summary.DormantApplications,
            ExpiringKeys = summary.ExpiringSecretKeys,
            FailedAuthenticationsLast24Hours = summary.FailedAuthenticationsLast24Hours,
            RecentEvents = [.. summary.RecentSecurityEvents.Select(V3AdminMapper.ToModel)]
        });
    });

    /// <summary>The audit trail across the realm: who did what, to what, with what result.</summary>
    [HttpGet("audit")]
    public Task<IActionResult> Audit(
        [FromQuery] string? eventType,
        [FromQuery] string? prefix,
        [FromQuery] string? clientId,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? result,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int take = 100) => AdminAsync(AdminPermissions.AuditRead, async _ =>
    {
        AuditEventQuery query = new()
        {
            EventTypes = string.IsNullOrWhiteSpace(eventType) ? [] : [eventType],
            EventTypePrefix = prefix,
            ClientId = clientId,
            UserId = userId?.ToString(),
            ActorUserId = actorUserId?.ToString(),
            Result = result,
            From = from,
            To = to,
            Take = Math.Clamp(take, 1, AuditEventQuery.MaxTake)
        };

        List<AuditEventDocument> events = await Service<IAuditEventRepository>().QueryAsync(query, Cancellation);

        return Ok(new V3AdminAuditPageResponse { Events = [.. events.Select(V3AdminMapper.ToModel)] });
    });
}

[Route("api/v3/admin/users")]
public sealed class V3AdminUsersController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet]
    public Task<IActionResult> Search([FromQuery] string? term, [FromQuery] int skip = 0, [FromQuery] int take = 25) =>
        AdminAsync(AdminPermissions.UsersRead, async _ =>
        {
            IReadOnlyList<AdminUserSummary> users = await Service<AdminUserService>().SearchAsync(term, Math.Max(0, skip), Math.Clamp(take, 1, 100), Cancellation);
            return Ok(users.Select(V3AdminMapper.ToModel));
        });

    [HttpGet("{userId:guid}")]
    public Task<IActionResult> Get(Guid userId) => AdminAsync(AdminPermissions.UsersRead, async _ =>
        Ok(V3AdminMapper.ToModel(await Service<AdminUserService>().GetDetailAsync(userId, Cancellation))));

    [HttpPost("{userId:guid}/disabled")]
    public Task<IActionResult> SetDisabled(Guid userId, [FromBody] V3AdminSetDisabledRequest request) =>
        AdminAsync(AdminPermissions.UsersManage, async access =>
            Ok(V3AdminMapper.ToModel(await Service<AdminUserService>().SetDisabledAsync(access.UserId, userId, request.Disabled, Cancellation))));

    [HttpGet("{userId:guid}/sessions")]
    public Task<IActionResult> Sessions(Guid userId) => AdminAsync(AdminPermissions.SessionsRead, async _ =>
        Ok((await Service<AdminSessionService>().ListForUserAsync(userId, Cancellation)).Select(V3AdminMapper.ToModel)));

    [HttpPost("{userId:guid}/sessions/revoke")]
    public Task<IActionResult> RevokeSessions(Guid userId) => AdminAsync(AdminPermissions.SessionsRevoke, async access =>
    {
        await Service<AdminSessionService>().RevokeAllForUserAsync(access.UserId, userId, Cancellation);
        return NoContent();
    });

    /// <summary>Ends every session and rotates the account's security stamp, so the person signs back in everywhere.</summary>
    [HttpPost("{userId:guid}/sign-out-everywhere")]
    public Task<IActionResult> SignOutEverywhere(Guid userId) => AdminAsync(AdminPermissions.SessionsRevoke, async access =>
    {
        await Service<AdminSessionService>().SignOutEverywhereAsync(access.UserId, userId, Cancellation);
        return NoContent();
    });

    [HttpDelete("{userId:guid}/passkeys/{credentialId}")]
    public Task<IActionResult> RevokePasskey(Guid userId, string credentialId) => AdminAsync(AdminPermissions.UsersManage, async access =>
    {
        await Service<AdminUserService>().RevokePasskeyAsync(access.UserId, userId, credentialId, Cancellation);
        return NoContent();
    });
}

[Route("api/v3/admin/sessions")]
public sealed class V3AdminSessionsController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? audience, [FromQuery] Guid? userId, [FromQuery] int take = 100) =>
        AdminAsync(AdminPermissions.SessionsRead, async _ =>
            Ok((await Service<AdminSessionService>().ListActiveAsync(audience, userId, Math.Clamp(take, 1, 500), Cancellation)).Select(V3AdminMapper.ToModel)));

    [HttpPost("{familyId}/revoke")]
    public Task<IActionResult> Revoke(string familyId) => AdminAsync(AdminPermissions.SessionsRevoke, async access =>
    {
        await Service<AdminSessionService>().RevokeAsync(access.UserId, familyId, Cancellation);
        return NoContent();
    });
}

[Route("api/v3/admin/administrators")]
public sealed class V3AdministratorsController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet]
    public Task<IActionResult> List() => AdminAsync(AdminPermissions.AdministratorsRead, async _ =>
        Ok((await Service<AdministratorService>().ListAsync(Cancellation)).Select(V3AdminMapper.ToModel)));

    /// <summary>Replaces the roles an account holds. An empty list removes administrator access.</summary>
    [HttpPut("{userId:guid}/roles")]
    public Task<IActionResult> SetRoles(Guid userId, [FromBody] V3AdminSetRolesRequest request) =>
        AdminAsync(AdminPermissions.AdministratorsManage, async access =>
            Ok(V3AdminMapper.ToModel(await Service<AdministratorService>().SetRolesAsync(
                access.UserId, userId, ParseEnums<AdminRoles>(request.Roles, "role") ?? [], Cancellation))));
}

[Route("api/v3/admin")]
public sealed class V3AdminSecurityController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet("keys")]
    public Task<IActionResult> Keys() => AdminAsync(AdminPermissions.KeysRead, async _ =>
    {
        SigningKeyOverview overview = await Service<SigningKeyAdminService>().GetOverviewAsync(Cancellation);

        return Ok(new V3AdminKeysResponse
        {
            ManagedInKeyVault = overview.ManagedInKeyVault,
            Keys = [.. overview.Keys.Select(V3AdminMapper.ToModel)]
        });
    });

    /// <summary>Starts signing with a new key now. The outgoing key keeps verifying through its publication window.</summary>
    [HttpPost("keys/rotate")]
    public Task<IActionResult> RotateKey() => AdminAsync(AdminPermissions.KeysManage, async access =>
        Ok(V3AdminMapper.ToModel(await Service<SigningKeyAdminService>().RotateAsync(access.UserId, Cancellation))));

    [HttpGet("providers")]
    public Task<IActionResult> Providers() => AdminAsync(AdminPermissions.ProvidersRead, async _ =>
        Ok((await Service<ProviderAdminService>().ListAsync(Cancellation)).Select(provider => new V3AdminProviderResponse
        {
            Code = provider.Code,
            Label = provider.Label,
            IsExternal = provider.IsExternal,
            VerifiesCredentials = provider.VerifiesCredentials,
            LinkedIdentities = provider.LinkedIdentities
        })));

    [HttpGet("providers/{code}/users")]
    public Task<IActionResult> ProviderUsers(string code, [FromQuery] int take = 100) => AdminAsync(AdminPermissions.ProvidersRead, async _ =>
        Ok((await Service<ProviderAdminService>().GetLinkedUsersAsync(code, take, Cancellation)).Select(user => new V3AdminProviderUserModel
        {
            UserId = user.UserId,
            DisplayName = user.DisplayName,
            ProviderEmail = user.ProviderEmail,
            LinkedOn = user.LinkedOn
        })));
}
