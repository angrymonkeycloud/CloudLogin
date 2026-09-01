using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AngryMonkey.CloudLogin.API.V3;

/// <summary>
/// RFC 8628 device authorization (QR / TV sign-in). The device polls with its
/// <c>device_code</c>; a signed-in person approves on their own device after seeing the user
/// code and the client description. A successful poll hands back a single-use login request id
/// that completes through the standard handoff — QR is transport, never an identity provider.
/// </summary>
[Route("api/v3/device")]
public sealed class V3DeviceController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    [HttpPost("authorize")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<V3DeviceAuthorizationResponse>> Begin([FromQuery] string? profile, [FromQuery] string? client)
    {
        SetNoStore();

        DeviceAuthorizationService? devices = CoreService<DeviceAuthorizationService>();
        if (devices is null)
            return CoreUnavailable();

        if (string.IsNullOrWhiteSpace(client) ||
            Configuration.Core?.DeviceAuthorization.Clients.TryGetValue(
                client, out string? clientDescription) != true ||
            string.IsNullOrWhiteSpace(clientDescription))
            return Unauthorized(new V3DeviceErrorResponse { Error = "unauthorized_client" });

        // The profile is resolved server-side now; URL edits cannot change the stored request.
        SignInProfileService? profiles = CoreService<SignInProfileService>();
        SignInProfileSelection? selection = profiles?.Resolve(profile, client);
        string? boundProfile = selection?.Profile.Name;

        string baseUrl = Configuration.BaseAddress ?? $"{Request.Scheme}://{Request.Host}";

        DeviceAuthorizationStart start = await devices.BeginAsync(
            baseUrl, client, clientDescription, boundProfile);

        return Ok(new V3DeviceAuthorizationResponse
        {
            DeviceCode = start.DeviceCode,
            UserCode = start.UserCode,
            VerificationUri = start.VerificationUri,
            VerificationUriComplete = start.VerificationUriComplete,
            ExpiresIn = start.ExpiresInSeconds,
            Interval = start.IntervalSeconds
        });
    }

    [HttpPost("token")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult> Poll([FromBody] V3DevicePollRequest request)
    {
        SetNoStore();

        DeviceAuthorizationService? devices = CoreService<DeviceAuthorizationService>();
        if (devices is null)
            return CoreUnavailable();

        if (string.IsNullOrWhiteSpace(request.DeviceCode))
            return BadRequest(new V3DeviceErrorResponse { Error = "invalid_request" });

        DevicePollResult result = await devices.PollAsync(request.DeviceCode);

        switch (result.Outcome)
        {
            case DevicePollOutcomes.Approved when result.RequestId is { } requestId:
                return Ok(new V3DevicePollSuccessResponse { RequestId = requestId });

            case DevicePollOutcomes.AuthorizationPending:
                return BadRequest(new V3DeviceErrorResponse { Error = "authorization_pending" });

            case DevicePollOutcomes.SlowDown:
                return BadRequest(new V3DeviceErrorResponse { Error = "slow_down" });

            case DevicePollOutcomes.ExpiredToken:
                return BadRequest(new V3DeviceErrorResponse { Error = "expired_token" });

            default:
                return BadRequest(new V3DeviceErrorResponse { Error = "access_denied" });
        }
    }

    [HttpGet("pending")]
    [Authorize]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<V3DevicePendingResponse>> GetPending([FromQuery(Name = "user_code")] string userCode)
    {
        SetNoStore();

        DeviceAuthorizationService? devices = CoreService<DeviceAuthorizationService>();
        if (devices is null)
            return CoreUnavailable();

        if (string.IsNullOrWhiteSpace(userCode))
            return BadRequest();

        DeviceApprovalView? pending = await devices.GetPendingByUserCodeAsync(userCode);

        if (pending is null)
            return NotFound();

        return Ok(new V3DevicePendingResponse
        {
            UserCode = pending.UserCode,
            ClientDescription = pending.ClientDescription,
            ExpiresOn = pending.ExpiresOn
        });
    }

    [HttpPost("approve")]
    [Authorize]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult> Approve([FromBody] V3DeviceDecisionRequest request)
    {
        SetNoStore();

        DeviceAuthorizationService? devices = CoreService<DeviceAuthorizationService>();
        if (devices is null)
            return CoreUnavailable();

        CloudUser? user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();

        // Approval requires the person to explicitly confirm the requesting client they saw.
        if (!request.ConfirmClient)
            return BadRequest(new ProblemDetails
            {
                Title = "Confirmation required",
                Detail = "Set confirmClient after verifying the requesting device description."
            });

        // How this person signed in on the device they are approving from, read from their own
        // ticket. The pending request's sign-in profile decides whether that method is good
        // enough to approve — the TV's profile governs the whole flow, not just the TV's screen.
        string? approvingMethod = User.FindFirst(CloudLoginAuthenticationClaims.AuthenticationMethod)?.Value;

        bool approved = await devices.ApproveAsync(request.UserCode, user.ID, approvingMethod);
        return approved ? NoContent() : NotFound();
    }

    [HttpPost("deny")]
    [Authorize]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult> Deny([FromBody] V3DeviceDecisionRequest request)
    {
        SetNoStore();

        DeviceAuthorizationService? devices = CoreService<DeviceAuthorizationService>();
        if (devices is null)
            return CoreUnavailable();

        CloudUser? user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();

        bool denied = await devices.DenyAsync(request.UserCode, user.ID);
        return denied ? NoContent() : NotFound();
    }
}
