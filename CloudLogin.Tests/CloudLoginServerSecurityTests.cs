using AngryMonkey.CloudLogin.Server;
using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// What a security change must never do is sign out the person making it. Every one of these
/// changes rotates the account's security stamp, which is how every <em>other</em> device gets
/// signed out - but the ticket in the browser that made the change carried the old stamp too,
/// so the next request found no user while the page still said "signed in". The fix re-issues
/// that one ticket; these tests pin it, along with the sign-in history the local sign-in paths
/// never wrote.
/// </summary>
public class CloudLoginServerSecurityTests
{
    private const string WindowsChrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    [Fact]
    public async Task PasswordLogin_records_the_sign_in_in_the_history()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.HttpContext.Request.Headers.UserAgent = WindowsChrome;

        Assert.True(await fixture.Server.PasswordLogin(
            CloudLoginPasswordLoginRequest.Create("person@example.com", "Valid#123456")));

        CloudLoginHistoryEntry entry = Assert.Single(fixture.SecurityStore.History[user.ID]);
        Assert.Equal("Password", entry.Provider);
        Assert.Equal("Chrome on Windows", entry.Device);
        Assert.Equal(WindowsChrome, entry.UserAgent);
    }

    [Fact]
    public async Task TestLogin_records_the_sign_in_in_the_history()
    {
        LoginTestFixture fixture = new(testModeEnabled: true);
        CloudUser user = await fixture.AddPasswordUserAsync(isTest: true);

        Assert.True(await fixture.Server.TestLogin(user.ID));

        Assert.Equal("TestMode", Assert.Single(fixture.SecurityStore.History[user.ID]).Provider);
    }

    [Fact]
    public async Task ChangeMyPassword_keeps_the_caller_signed_in_on_the_new_stamp()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.Store.SecurityStamps[user.ID] = "stamp-1";
        fixture.AuthenticateAs(user);
        Assert.NotNull(await fixture.Server.CurrentUser());

        await fixture.Server.ChangeMyPassword(new CloudLoginChangePasswordRequest
        {
            CurrentPassword = "Valid#123456",
            NewPassword = "Another#654321"
        });

        // The stamp rotated - every other device is out - and this browser got a fresh ticket
        // carrying it, so it is still signed in.
        string rotated = fixture.Store.SecurityStamps[user.ID];
        Assert.NotEqual("stamp-1", rotated);
        Assert.Equal(1, fixture.Authentication.SignInCount);
        Assert.Equal(rotated, fixture.Authentication.SignedInPrincipal!.FindFirstValue(CloudLoginAuthenticationClaims.SecurityStamp));
        Assert.Equal(user.ID.ToString(), fixture.Authentication.SignedInPrincipal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.NotNull(await fixture.Server.CurrentUser());
    }

    [Fact]
    public async Task A_ticket_with_a_stale_stamp_is_no_longer_a_signed_in_user()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.Store.SecurityStamps[user.ID] = "stamp-1";
        fixture.AuthenticateAs(user);

        // Rotated from somewhere else: an admin lock, a password change on another device.
        await fixture.Store.RotateSecurityStamp(user.ID);

        Assert.Null(await fixture.Server.CurrentUser());
    }

    [Fact]
    public async Task Disabling_the_authenticator_re_issues_the_callers_ticket()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.Store.SecurityStamps[user.ID] = "stamp-1";
        fixture.AuthenticateAs(user);
        await fixture.SecurityStore.UpdateCredentials(user.ID, document => document.Authenticator = new CloudLoginAuthenticatorApp
        {
            SecretKey = "JBSWY3DPEHPK3PXP",
            EnrolledOn = DateTimeOffset.UtcNow,
            IsConfirmed = true
        });

        await fixture.Server.DisableAuthenticator();

        Assert.Null((await fixture.SecurityStore.GetCredentials(user.ID)).Authenticator);
        Assert.Equal(1, fixture.Authentication.SignInCount);
        Assert.NotNull(await fixture.Server.CurrentUser());
    }

    /// <summary>The re-issued ticket is the same device: its session travels along.</summary>
    [Fact]
    public async Task A_re_issued_ticket_keeps_its_browser_session()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.Store.SecurityStamps[user.ID] = "stamp-1";
        fixture.AuthenticateAs(user);
        CloudLoginAuthenticationClaims.WithSession(fixture.HttpContext.User, "sess_1", "family_1");

        await fixture.Server.ChangeMyPassword(new CloudLoginChangePasswordRequest
        {
            CurrentPassword = "Valid#123456",
            NewPassword = "Another#654321"
        });

        (string? sessionId, string? familyId) = CloudLoginAuthenticationClaims.SessionOf(fixture.Authentication.SignedInPrincipal);
        Assert.Equal("sess_1", sessionId);
        Assert.Equal("family_1", familyId);
    }
}
