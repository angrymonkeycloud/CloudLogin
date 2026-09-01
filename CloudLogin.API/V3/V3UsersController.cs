using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AngryMonkey.CloudLogin.API.V3;

[Route("api/v3/users")]
public sealed class V3UsersController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    // ── Self ──────────────────────────────────────────────────────────────────

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<V3SelfProfileResponse>> GetMe()
    {
        CloudUser? user = await CurrentUserAsync();
        return user is null ? Unauthorized() : Ok(ToSelfProfile(user));
    }

    [HttpPatch("me")]
    [Authorize]
    public async Task<ActionResult<V3SelfProfileResponse>> UpdateMe([FromBody] V3UpdateProfileRequest request)
    {
        CloudUser? user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();

        CloudUser? stored = await Server.GetUserById(user.ID);
        if (stored is null)
            return NotFound();

        // Whitelist only. Privileges, providers, identifiers, and lock state are server-managed.
        stored.FirstName = request.FirstName ?? stored.FirstName;
        stored.LastName = request.LastName ?? stored.LastName;
        stored.DisplayName = request.DisplayName ?? stored.DisplayName;
        stored.Username = request.Username ?? stored.Username;
        stored.DateOfBirth = request.DateOfBirth ?? stored.DateOfBirth;
        stored.Country = request.Country ?? stored.Country;
        stored.Locale = request.Locale ?? stored.Locale;

        await Server.UpdateUser(stored);

        CloudUser? updated = await Server.GetUserById(user.ID);
        return Ok(ToSelfProfile(updated!));
    }

    // ── Public summary (authorized; V3 has no anonymous user detail) ─────────

    [HttpGet("{userId:guid}/summary")]
    [Authorize]
    public async Task<ActionResult<V3PublicUserSummaryResponse>> GetSummary(Guid userId)
    {
        CloudUser? user = await Server.GetUserById(userId);
        return user is null ? NotFound() : Ok(ToPublicSummary(user));
    }

    // ── Login discovery (anonymous by necessity; rate limited; minimal reply) ─

    [HttpPost("discovery")]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public async Task<ActionResult<V3LoginDiscoveryResponse>> Discover(
        [FromBody] V3LoginDiscoveryRequest request, [FromQuery] string? profile, [FromQuery] string? client)
    {
        SetNoStore();

        if (string.IsNullOrWhiteSpace(request.Identifier))
            return BadRequest();

        CloudUser? user = await Server.GetUserByInput(request.Identifier);

        List<string> methods = [];

        if (user is not null)
        {
            methods = [.. user.Providers];

            SignInProfileService? profiles = CoreService<SignInProfileService>();
            if (profiles is not null)
            {
                CloudLoginSignInProfile resolved = profiles.Resolve(profile, client).Profile;
                methods = [.. methods.Where(method => SignInProfileService.AllowsMethod(resolved, method))];
            }
        }

        return Ok(new V3LoginDiscoveryResponse
        {
            AccountExists = user is not null,
            AvailableMethods = methods
        });
    }

    // ── Administrator views ───────────────────────────────────────────────────

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<V3AdminUserResponse>>> GetAll()
    {
        if (!await IsGlobalAdminAsync())
            return Forbid();

        List<CloudUser> users = await Server.GetAllUsers();
        return Ok(users.Select(ToAdminView).ToList());
    }

    [HttpGet("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<V3AdminUserResponse>> GetById(Guid userId)
    {
        if (!await IsGlobalAdminAsync())
            return Forbid();

        CloudUser? user = await Server.GetUserById(userId);
        return user is null ? NotFound() : Ok(ToAdminView(user));
    }

    private async Task<bool> IsGlobalAdminAsync() => (await CurrentUserAsync())?.IsGlobalAdmin == true;
}
