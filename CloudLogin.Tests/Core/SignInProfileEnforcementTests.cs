using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using Microsoft.Extensions.Primitives;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// A sign-in profile is only worth something if every entry path honours it. Resolution itself
/// is covered by <see cref="SignInProfileTests"/>; these tests check that the restriction
/// actually reaches password, verification-code and QR sign-in — not just the provider redirect
/// that has always carried a sealed profile in its ticket.
/// </summary>
public class SignInProfileEnforcementTests
{
    private const string TvClient = "https://tv.example";

    /// <summary>A profile that allows only a provider — no password, no code.</summary>
    private static SignInProfileConfiguration ProviderOnly() => new()
    {
        Profiles =
        [
            new CloudLoginSignInProfile { Name = "default" },
            new CloudLoginSignInProfile
            {
                Name = "tv",
                VisibleMethods = [SignInProfileConfiguration.QrMethod],
                AllowedMethods = ["Google"]
            }
        ],
        ClientProfiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [TvClient] = ["tv"]
        }
    };

    private static LoginTestFixture BuildFixture() => new(
        allowedOrigins: [TvClient],
        signInProfiles: ProviderOnly());

    /// <summary>Puts the profile on the request the way the login page navigates with it.</summary>
    private static void RequestProfile(LoginTestFixture fixture, string profile, string client) =>
        fixture.HttpContext.Request.QueryString =
            new Microsoft.AspNetCore.Http.QueryString($"?profile={profile}&client={Uri.EscapeDataString(client)}");

    // ── Password ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PasswordSignIn_IsRefusedByAProfileThatDoesNotAllowIt()
    {
        LoginTestFixture fixture = BuildFixture();
        await fixture.AddPasswordUserAsync("person@example.com", "Correct-Horse-1");
        RequestProfile(fixture, "tv", TvClient);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Server.PasswordLogin(
            CloudLoginPasswordLoginRequest.Create("person@example.com", "Correct-Horse-1", false)));
    }

    [Fact]
    public async Task PasswordSignIn_ProceedsUnderTheDefaultProfile()
    {
        LoginTestFixture fixture = BuildFixture();
        await fixture.AddPasswordUserAsync("person@example.com", "Correct-Horse-1");

        Assert.True(await fixture.Server.PasswordLogin(
            CloudLoginPasswordLoginRequest.Create("person@example.com", "Correct-Horse-1", false)));
    }

    [Fact]
    public async Task PasswordSignIn_ReadsTheProfileFromAPostedForm()
    {
        // The password form posts rather than navigating, so a query-string-only lookup would
        // miss the profile entirely and let the restricted method through.
        LoginTestFixture fixture = BuildFixture();
        await fixture.AddPasswordUserAsync("person@example.com", "Correct-Horse-1");

        fixture.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
        fixture.HttpContext.Request.Form = new Microsoft.AspNetCore.Http.FormCollection(
            new Dictionary<string, StringValues>
            {
                ["profile"] = "tv",
                ["client"] = TvClient
            });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Server.PasswordLogin(
            CloudLoginPasswordLoginRequest.Create("person@example.com", "Correct-Horse-1", false)));
    }

    [Fact]
    public async Task AnUnauthorizedProfileRequest_FallsBackToTheDefault_AndCanOnlyNarrow()
    {
        // "tv" is not allowed for this client, so resolution falls back to the default profile,
        // which restricts nothing. A forged parameter therefore cannot unlock anything.
        LoginTestFixture fixture = BuildFixture();
        await fixture.AddPasswordUserAsync("person@example.com", "Correct-Horse-1");
        RequestProfile(fixture, "tv", "https://someone-elses.example");

        Assert.True(await fixture.Server.PasswordLogin(
            CloudLoginPasswordLoginRequest.Create("person@example.com", "Correct-Horse-1", false)));
    }

    // ── Provider ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProviderChallenge_ForADisallowedProvider_IsNotFound()
    {
        LoginTestFixture fixture = BuildFixture();
        RequestProfile(fixture, "tv", TvClient);

        Microsoft.AspNetCore.Mvc.IActionResult result = await fixture.Server.Login(
            "facebook", false, false, referer: TvClient);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    // ── QR / device approval ──────────────────────────────────────────────────

    [Fact]
    public async Task DeviceApproval_FromASessionTheProfileDoesNotAllow_IsRefused()
    {
        // The TV asked for a profile that only accepts Google. Approving from a session that was
        // started with a password must not satisfy it, or the restriction is decorative.
        (DeviceAuthorizationService devices, DeviceAuthorizationStart start) = await BeginTvRequestAsync();

        Assert.False(await devices.ApproveAsync(start.UserCode, Guid.NewGuid(), approvingMethod: "Password"));
    }

    [Fact]
    public async Task DeviceApproval_FromAnAllowedSession_Succeeds()
    {
        (DeviceAuthorizationService devices, DeviceAuthorizationStart start) = await BeginTvRequestAsync();

        Assert.True(await devices.ApproveAsync(start.UserCode, Guid.NewGuid(), approvingMethod: "Google"));
    }

    [Fact]
    public async Task DeviceApproval_WithNoMethodReported_IsNotBlocked()
    {
        // A caller that cannot report how the person signed in gets the previous behavior rather
        // than a refusal it has no way to satisfy.
        (DeviceAuthorizationService devices, DeviceAuthorizationStart start) = await BeginTvRequestAsync();

        Assert.True(await devices.ApproveAsync(start.UserCode, Guid.NewGuid()));
    }

    private static async Task<(DeviceAuthorizationService Devices, DeviceAuthorizationStart Start)> BeginTvRequestAsync()
    {
        InMemoryLoginRequestRepository requests = new();
        InMemoryAuditEventRepository audit = new();
        CloudLoginCoreConfiguration core = new();

        DeviceAuthorizationService devices = new(
            requests, core, new AuditLogger(audit, core), ProviderOnly());

        DeviceAuthorizationStart start = await devices.BeginAsync(
            "https://login.example", TvClient, "Living room TV", "tv");

        return (devices, start);
    }
}
