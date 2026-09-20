using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// What the account's Security page offers depends on whether this authority signs people in
/// itself.
/// </summary>
/// <remarks>
/// An authority configured with only external providers never checks a credential of its own, so a
/// password, an authenticator app and a passkey would all guard a sign-in that happens somewhere
/// else. Offering them is worse than useless: it invites people to set up protection that nothing
/// will ever ask for.
/// </remarks>
public class AccountSecurityVisibilityTests
{
    // ── The rule itself ───────────────────────────────────────────────────────

    private static CloudLoginProviderDefinition Definition(
        string code, bool isExternal, bool isCodeVerification) =>
        new(code)
        {
            HandlesEmailAddress = true,
            HandlesPhoneNumber = false,
            IsCodeVerification = isCodeVerification,
            InputRequired = true,
            IsExternal = isExternal
        };

    [Theory]
    [InlineData("password", false, false, true)]
    [InlineData("code", false, true, true)]
    [InlineData("whatsapp", false, true, true)]
    [InlineData("google", true, false, false)]
    [InlineData("microsoft", true, false, false)]
    // Test mode signs anybody in without checking anything, so it is not a credential and must not
    // make the account offer credential settings.
    [InlineData("testmode", false, false, false)]
    public void OnlyProvidersThatCheckACredential_CountAsLocalSignIn(
        string code, bool isExternal, bool isCodeVerification, bool expected) =>
        Assert.Equal(expected, Definition(code, isExternal, isCodeVerification).VerifiesCredentials);

    [Fact]
    public void LocalSignInConfigured_FollowsEitherProvider()
    {
        Assert.False(new CloudLoginSecurityOverview().LocalSignInConfigured);
        Assert.True(new CloudLoginSecurityOverview { PasswordProviderConfigured = true }.LocalSignInConfigured);
        Assert.True(new CloudLoginSecurityOverview { CodeProviderConfigured = true }.LocalSignInConfigured);
    }

    // ── What the page renders ─────────────────────────────────────────────────

    private static async Task<string> RenderSecurityAsync(CloudLoginSecurityOverview overview)
    {
        ICloudLogin login = DispatchProxy.Create<ICloudLogin, SecurityProxy>();
        ((SecurityProxy)login).Overview = overview;

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(login);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IJSRuntime>(new NoJavaScript());

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<AccountComponent_Security>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["CurrentUser"] = new CloudUserModel(),
                    ["DevicesOnly"] = false
                }));

            return root.ToHtmlString();
        });
    }

    [Fact]
    public async Task WithNoLocalProvider_CredentialSectionsAreHidden()
    {
        string html = await RenderSecurityAsync(new CloudLoginSecurityOverview());

        Assert.DoesNotContain("Password", html);
        Assert.DoesNotContain("Authenticator app", html);
        Assert.DoesNotContain("Passkeys", html);

        // What remains is everything that is still true of a federated account.
        Assert.Contains("Sign-in providers", html);
        Assert.Contains("Your devices", html);
    }

    [Fact]
    public async Task WithThePasswordProvider_EveryCredentialSectionIsOffered()
    {
        string html = await RenderSecurityAsync(new CloudLoginSecurityOverview { PasswordProviderConfigured = true });

        Assert.Contains("Add password", html);
        Assert.Contains("Authenticator app", html);
        Assert.Contains("Passkeys", html);
    }

    [Fact]
    public async Task WithOnlyACodeProvider_TheSecondFactorsShowButThePasswordCardDoesNot()
    {
        // A code provider is a credential this authority checks, so a second factor has something
        // to guard - but there is still no password, and offering to set one would be a lie.
        string html = await RenderSecurityAsync(new CloudLoginSecurityOverview { CodeProviderConfigured = true });

        Assert.Contains("Authenticator app", html);
        Assert.Contains("Passkeys", html);
        Assert.DoesNotContain("Add password", html);
        Assert.DoesNotContain("Change password", html);
    }

    public class SecurityProxy : DispatchProxy
    {
        public CloudLoginSecurityOverview Overview { get; set; } = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "GetSecurityOverview" => Task.FromResult(Overview),
            "GetMyDevices" => Task.FromResult(new List<CloudLoginSignedInDevice>()),
            "GetMyLoginHistory" => Task.FromResult(new List<CloudLoginHistoryEntry>()),
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/Account");

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(T)!);
    }
}
