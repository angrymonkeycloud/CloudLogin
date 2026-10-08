using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.V3.Admin;

/// <summary>The applications seen using CloudLogin, and the ways an administrator stops one.</summary>
[Route("api/v3/admin/applications")]
public sealed class V3AdminApplicationsController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet]
    public Task<IActionResult> List() => AdminAsync(AdminPermissions.ApplicationsRead, async _ =>
    {
        ApplicationService applications = Service<ApplicationService>();
        return Ok((await applications.ListAsync(Cancellation)).Select(view => V3AdminMapper.ToModel(view, applications.IsDormant(view.Document))));
    });

    [HttpGet("{clientId}")]
    public Task<IActionResult> Get(string clientId) => AdminAsync(AdminPermissions.ApplicationsRead, async _ =>
    {
        ApplicationService applications = Service<ApplicationService>();
        ApplicationView view = await applications.GetAsync(clientId, Cancellation);
        return Ok(V3AdminMapper.ToModel(view, applications.IsDormant(view.Document)));
    });

    /// <summary>Refuses the application from now on and ends every session it holds.</summary>
    [HttpPost("{clientId}/block")]
    public Task<IActionResult> Block(string clientId, [FromBody] V3AdminBlockApplicationRequest? request) => AdminAsync(AdminPermissions.ApplicationsManage, async access =>
        Ok(new V3AdminRevokedCountResponse { Revoked = await Service<ApplicationService>().BlockAsync(access.UserId, clientId, request?.Reason, Cancellation) }));

    [HttpPost("{clientId}/unblock")]
    public Task<IActionResult> Unblock(string clientId) => AdminAsync(AdminPermissions.ApplicationsManage, async access =>
    {
        await Service<ApplicationService>().UnblockAsync(access.UserId, clientId, Cancellation);
        return NoContent();
    });

    /// <summary>Removes the record of an application. It reappears if it signs someone in again.</summary>
    [HttpDelete("{clientId}")]
    public Task<IActionResult> Forget(string clientId) => AdminAsync(AdminPermissions.ApplicationsManage, async access =>
    {
        await Service<ApplicationService>().ForgetAsync(access.UserId, clientId, Cancellation);
        return NoContent();
    });

    [HttpGet("{clientId}/sessions")]
    public Task<IActionResult> Sessions(string clientId, [FromQuery] int take = 100) => AdminAsync(AdminPermissions.SessionsRead, async _ =>
    {
        ApplicationView application = await Service<ApplicationService>().GetAsync(clientId, Cancellation);
        IReadOnlyList<AdminSessionView> sessions = await Service<AdminSessionService>().ListActiveAsync(application.Document.ClientId, null, Math.Clamp(take, 1, 500), Cancellation);

        return Ok(sessions.Select(V3AdminMapper.ToModel));
    });

    /// <summary>Ends every session the application holds without blocking it.</summary>
    [HttpPost("{clientId}/sessions/revoke")]
    public Task<IActionResult> RevokeSessions(string clientId) => AdminAsync(AdminPermissions.SessionsRevoke, async access =>
        Ok(new V3AdminRevokedCountResponse { Revoked = await Service<ApplicationService>().RevokeSessionsAsync(access.UserId, clientId, Cancellation) }));
}

/// <summary>The secret keys websites with a backend authenticate with.</summary>
[Route("api/v3/admin/secret-keys")]
public sealed class V3AdminSecretKeysController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3AdminControllerBase(configuration, server)
{
    [HttpGet]
    public Task<IActionResult> List() => AdminAsync(AdminPermissions.ApplicationsRead, async _ =>
        Ok((await Service<SecretKeyService>().ListAsync(Cancellation)).Select(V3AdminMapper.ToModel)));

    /// <summary>Creates a key. Its secret is in this response only and cannot be read again.</summary>
    [HttpPost]
    public Task<IActionResult> Create([FromBody] V3AdminCreateSecretKeyRequest request) => AdminAsync(AdminPermissions.SecretKeysManage, async access =>
    {
        IssuedSecretKey issued = await Service<SecretKeyService>().CreateAsync(access.UserId, request.Label, request.ExpiresOn, Cancellation);
        return Ok(new V3AdminIssuedSecretKeyResponse { Key = V3AdminMapper.ToModel(new SecretKeyView(issued.Key, SecretKeyStatuses.Active)), KeyValue = issued.Secret });
    });

    /// <summary>Stops the key working at once, for every website using it.</summary>
    [HttpPost("{keyId}/revoke")]
    public Task<IActionResult> Revoke(string keyId, [FromBody] V3AdminRevokeRequest? request) => AdminAsync(AdminPermissions.SecretKeysRevoke, async access =>
    {
        await Service<SecretKeyService>().RevokeAsync(access.UserId, keyId, request?.Reason, Cancellation);
        return NoContent();
    });

    [HttpGet("expiring")]
    public Task<IActionResult> Expiring([FromQuery] int withinDays = 30) => AdminAsync(AdminPermissions.ApplicationsRead, async _ =>
        Ok((await Service<SecretKeyService>().GetExpiringAsync(withinDays, Cancellation)).Select(V3AdminMapper.ToModel)));
}
