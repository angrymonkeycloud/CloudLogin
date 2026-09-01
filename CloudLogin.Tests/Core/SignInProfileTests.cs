using AngryMonkey.CloudLogin.Server.Core.Application;
using Microsoft.AspNetCore.DataProtection;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class SignInProfileTests
{
    private static SignInProfileConfiguration BuildConfiguration() => new()
    {
        Profiles =
        [
            new CloudLoginSignInProfile { Name = "default" },
            new CloudLoginSignInProfile
            {
                Name = "tv",
                VisibleMethods = [SignInProfileConfiguration.QrMethod],
                AllowedMethods = ["Password", "Google"]
            },
            new CloudLoginSignInProfile
            {
                Name = "passwordless",
                VisibleMethods = ["Code", "Google"],
                AllowedMethods = ["Code", "Google"]
            }
        ],
        ClientProfiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://tv.example"] = ["tv"],
            ["https://app.example"] = ["passwordless"]
        }
    };

    private static SignInProfileService BuildService(SignInProfileConfiguration? configuration = null) =>
        new(configuration ?? BuildConfiguration(), new EphemeralDataProtectionProvider());

    [Fact]
    public void Resolve_NoProfileRequested_UsesDefault()
    {
        SignInProfileSelection selection = BuildService().Resolve(null, "https://tv.example");

        Assert.Equal("default", selection.Profile.Name);
        Assert.False(selection.FellBackToDefault);
    }

    [Fact]
    public void Resolve_UnknownProfile_FailsSafelyToDefault()
    {
        SignInProfileSelection selection = BuildService().Resolve("kiosk", "https://tv.example");

        Assert.Equal("default", selection.Profile.Name);
        Assert.True(selection.FellBackToDefault);
    }

    [Fact]
    public void Resolve_ProfileNotAllowedForClient_FailsSafelyToDefault()
    {
        // The tv profile exists, but only https://tv.example was granted it.
        SignInProfileSelection selection = BuildService().Resolve("tv", "https://app.example");

        Assert.Equal("default", selection.Profile.Name);
        Assert.True(selection.FellBackToDefault);
    }

    [Fact]
    public void Resolve_UnknownClient_GetsOnlyDefault()
    {
        SignInProfileSelection selection = BuildService().Resolve("tv", "https://evil.example");

        Assert.Equal("default", selection.Profile.Name);
        Assert.True(selection.FellBackToDefault);
    }

    [Fact]
    public void Resolve_AllowedClient_GetsRequestedProfile()
    {
        SignInProfileSelection selection = BuildService().Resolve("tv", "https://tv.example");

        Assert.Equal("tv", selection.Profile.Name);
        Assert.False(selection.FellBackToDefault);
        Assert.Equal([SignInProfileConfiguration.QrMethod], selection.Profile.VisibleMethods);
    }

    [Fact]
    public void TvProfile_ShowsOnlyQr_ButApprovalUsesConfiguredMethods()
    {
        SignInProfileSelection selection = BuildService().Resolve("tv", "https://tv.example");

        // The TV page displays QR only; the mobile approval page authorizes with the
        // profile's underlying methods.
        Assert.DoesNotContain("Password", selection.Profile.VisibleMethods);
        Assert.True(SignInProfileService.AllowsMethod(selection.Profile, "Password"));
        Assert.True(SignInProfileService.AllowsMethod(selection.Profile, "Google"));
        Assert.False(SignInProfileService.AllowsMethod(selection.Profile, "Facebook"));
    }

    [Fact]
    public void BindAndUnbind_RoundTripsTheProfile()
    {
        SignInProfileService service = BuildService();
        SignInProfileSelection selection = service.Resolve("tv", "https://tv.example");

        string bound = service.Bind(selection, "https://tv.example");
        CloudLoginSignInProfile? unbound = service.Unbind(bound, "https://tv.example");

        Assert.NotNull(unbound);
        Assert.Equal("tv", unbound!.Name);
        Assert.Null(service.Unbind(bound, "https://different.example"));
    }

    [Fact]
    public void Unbind_TamperedState_ReturnsNullNeverAFallback()
    {
        SignInProfileService service = BuildService();
        SignInProfileSelection selection = service.Resolve("passwordless", "https://app.example");

        string bound = service.Bind(selection, "https://app.example");
        string tampered = bound[..^4] + "AAAA";

        Assert.Null(service.Unbind(tampered));
        Assert.Null(service.Unbind("garbage"));
    }

    [Fact]
    public void UrlParameters_CannotEnableMethodsOutsideConfiguration()
    {
        // A profile only ever narrows: resolution selects among configured profiles, and an
        // empty allowlist means "whatever the deployment configured" — nothing more.
        SignInProfileConfiguration configuration = BuildConfiguration();
        SignInProfileService service = BuildService(configuration);

        SignInProfileSelection selection = service.Resolve("passwordless", "https://app.example");

        Assert.False(SignInProfileService.AllowsMethod(selection.Profile, "Password"));
    }
}
