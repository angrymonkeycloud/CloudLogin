using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.V3;

/// <summary>
/// The devices the signed-in account is signed in on, and the ability to sign one out.
/// <para>
/// Always scoped to the caller: no endpoint here takes a user id, so this surface cannot be used
/// to read or revoke another account's sessions. A device is a refresh-token family — signing in
/// creates one, revoking it signs that device out and leaves the others alone.
/// </para>
/// </summary>
[Route("api/v3/devices")]
[Authorize]
public sealed class V3DevicesController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    [HttpGet]
    public async Task<ActionResult<List<V3SignedInDeviceResponse>>> GetMine()
    {
        SetNoStore();

        SessionService? sessions = CoreService<SessionService>();
        if (sessions is null)
            return CoreUnavailable();

        CloudUser? user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();

        List<SignedInDevice> devices = await sessions.GetDevicesAsync(user.ID, CurrentSessionId());

        return Ok(devices.Select(ToResponse).ToList());
    }

    /// <summary>Signs one device out. Answers 404 for an id that is not this account's.</summary>
    [HttpDelete("{deviceId}")]
    public async Task<ActionResult> SignOut(string deviceId)
    {
        SetNoStore();

        SessionService? sessions = CoreService<SessionService>();
        if (sessions is null)
            return CoreUnavailable();

        CloudUser? user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();

        // Ownership is checked inside the service, which answers false rather than throwing for
        // an id belonging to someone else — indistinguishable here from one that never existed.
        return await sessions.RevokeDeviceAsync(user.ID, deviceId) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Signs every other device out, keeping this one. Answers how many were revoked.
    /// <para>
    /// Which device to keep is decided from the caller's ticket, never from the request body — a
    /// client that could name the device to keep could name someone else's.
    /// </para>
    /// </summary>
    [HttpDelete]
    public async Task<ActionResult<V3RevokeOtherDevicesResponse>> SignOutOthers()
    {
        SetNoStore();

        SessionService? sessions = CoreService<SessionService>();
        if (sessions is null)
            return CoreUnavailable();

        CloudUser? user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();

        int revoked = await sessions.RevokeOtherDevicesAsync(user.ID, CurrentSessionId());

        return Ok(new V3RevokeOtherDevicesResponse { RevokedCount = revoked });
    }

    /// <summary>The caller's own session, read from the ticket rather than from the request.</summary>
    private string? CurrentSessionId() =>
        User.FindFirst(CloudLoginClaims.SessionId)?.Value;

    private static V3SignedInDeviceResponse ToResponse(SignedInDevice device) => new()
    {
        DeviceId = device.DeviceId,
        Name = device.Name,
        Type = device.Type.ToString(),
        Browser = device.Browser,
        OperatingSystem = device.OperatingSystem,
        SignedInFromIp = device.SignedInFromIp,
        LastSeenIp = device.LastSeenIp,
        SignedInOn = device.SignedInOn,
        LastSeenOn = device.LastSeenOn,
        ExpiresOn = device.ExpiresOn,
        IsActive = device.IsActive,
        // Only meaningful once a device is inactive; "None" on an active one would read as a fact.
        RevocationReason = device.IsActive ? null : device.RevocationReason.ToString(),
        RevokedOn = device.RevokedOn,
        IsCurrent = device.IsCurrent
    };
}
