using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AngryMonkey.CloudLogin.API.V3;

/// <summary>
/// Resolves the sign-in profile for a login page. The response's <c>BoundState</c> seals the
/// resolution with Data Protection; completion calls present it back, so a URL edit after this
/// point cannot change which profile governs the flow. URL parameters can only ever select
/// among configured profiles — never enable providers or capabilities directly.
/// </summary>
[Route("api/v3/signin-profile")]
public sealed class V3SignInProfilesController(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    [HttpGet]
    [EnableRateLimiting(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy)]
    public ActionResult<V3SignInProfileResponse> Resolve([FromQuery] string? profile, [FromQuery] string? client)
    {
        SetNoStore();

        SignInProfileService? profiles = CoreService<SignInProfileService>();
        if (profiles is null)
            return CoreUnavailable();

        SignInProfileSelection selection = profiles.Resolve(profile, client);

        List<string> configuredMethods = [.. Configuration.Providers.Select(provider => provider.Code)];
        configuredMethods.Add(SignInProfileConfiguration.QrMethod);

        List<string> visible = selection.Profile.VisibleMethods.Count == 0
            ? configuredMethods
            : [.. selection.Profile.VisibleMethods.Where(method =>
                configuredMethods.Contains(method, StringComparer.OrdinalIgnoreCase))];

        return Ok(new V3SignInProfileResponse
        {
            Name = selection.Profile.Name,
            VisibleMethods = visible,
            BoundState = profiles.Bind(selection, client)
        });
    }
}
