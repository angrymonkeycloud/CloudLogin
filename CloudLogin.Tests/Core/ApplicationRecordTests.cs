using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>Applications are recorded as they are seen, never created; an administrator can block, unblock or forget one.</summary>
public class ApplicationRecordTests
{
    private readonly ControlPlaneFixture _plane = new();

    [Fact]
    public async Task ABackend_IsRecordedTheFirstTimeItAuthenticates()
    {
        Assert.Empty(_plane.Applications.Documents);

        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);

        ApplicationDocument record = _plane.Applications.Documents["portal"];
        Assert.Equal(ApplicationKinds.Backend, record.Kind);
        Assert.NotEqual(default, record.FirstSeenOn);
        Assert.StartsWith(ClientDirectory.DeploymentKeyPrefix, record.LastSecretKeyId);
    }

    [Fact]
    public async Task AFailedAuthentication_RecordsNoApplication()
    {
        await _plane.AuthenticateAsync("portal", "wrong-secret-wrong-secret-wrong-secret");

        Assert.Empty(_plane.Applications.Documents);
        Assert.Contains(_plane.AuditEvents.Events, item => item.EventType == AuditEventTypes.ClientAuthenticationFailed);
    }

    [Fact]
    public async Task ABackendsOrigins_AndBackChannelAddress_AreRecordedAsItReportsThem()
    {
        await _plane.Directory.RecordBackendAsync("portal", "https://portal.example", "https://portal.example/auth/backchannel-logout");
        await _plane.Directory.RecordBackendAsync("portal", "https://preview-7.portal.example", "https://preview-7.portal.example/auth/backchannel-logout");

        ApplicationDocument record = _plane.Applications.Documents["portal"];
        Assert.Equal(["https://preview-7.portal.example", "https://portal.example"], record.Origins);
        Assert.Equal("https://preview-7.portal.example/auth/backchannel-logout", await _plane.Directory.GetBackChannelLogoutUriAsync("portal"));
    }

    [Theory]
    [InlineData("https://site.example/callback", "https://site.example", ApplicationKinds.Website)]
    [InlineData("https://SITE.example:8443/x", "https://site.example:8443", ApplicationKinds.Website)]
    [InlineData("http://localhost:5000/cb", "http://localhost:5000", ApplicationKinds.Website)]
    [InlineData("blusky://auth/callback", "blusky:", ApplicationKinds.NativeApp)]
    [InlineData("blusky:", "blusky:", ApplicationKinds.NativeApp)]
    public async Task APublicClient_IsIdentifiedByItsOwnOrigin(string redirect, string expectedId, ApplicationKinds kind)
    {
        ClientIdentity identity = (await _plane.Directory.AuthenticatePublicAsync(redirect)).Client!;

        Assert.Equal(expectedId, identity.ClientId);
        Assert.Equal(expectedId, identity.Audience);
        Assert.Equal(kind, identity.Kind);
        Assert.True(identity.IsPublic);
        Assert.Equal(kind == ApplicationKinds.NativeApp, identity.ReceivesRefreshTokens);
        Assert.Equal(kind, _plane.Applications.Documents[expectedId].Kind);
    }

    [Theory]
    [InlineData("http://not-loopback.example/cb")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("https://user:pass@site.example/cb")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task AnUnusablePublicAddress_IsRefused(string redirect)
    {
        Assert.False((await _plane.Directory.AuthenticatePublicAsync(redirect)).Succeeded);
    }

    [Fact]
    public async Task BlockingAnApplication_RefusesItFromThenOn_UntilUnblocked()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);

        await _plane.Apps.BlockAsync(_plane.Admin, "portal", "compromised");

        Assert.Equal(ClientAuthenticationFailures.Blocked, (await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
        Assert.Equal(ClientAuthenticationFailures.Blocked, (await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
        Assert.True((await _plane.AuthenticateAsync("blog", ControlPlaneFixture.DeploymentKey)).Succeeded);

        await _plane.Apps.UnblockAsync(_plane.Admin, "portal");

        Assert.True((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
    }

    [Fact]
    public async Task BlockingAPublicSite_RefusesItsSignIns()
    {
        await _plane.Directory.AuthenticatePublicAsync("https://site.example/callback");

        await _plane.Apps.BlockAsync(_plane.Admin, "https://site.example", null);

        Assert.Equal(ClientAuthenticationFailures.Blocked, (await _plane.Directory.AuthenticatePublicAsync("https://site.example/other")).Failure);
        Assert.True((await _plane.Directory.AuthenticatePublicAsync("https://another.example/callback")).Succeeded);
    }

    [Fact]
    public async Task BlockingAnApplication_EndsItsSessions_AndOnlyItsSessions()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        Guid user = Guid.NewGuid();
        SessionIssueResult portal = await _plane.SessionService.IssueFamilyAsync(user, "portal", null, null, null);
        SessionIssueResult blog = await _plane.SessionService.IssueFamilyAsync(user, "blog", null, null, null);

        int ended = await _plane.Apps.BlockAsync(_plane.Admin, "portal", null);

        Assert.Equal(1, ended);
        Assert.False(await _plane.SessionService.IsFamilyActiveAsync(portal.FamilyId));
        Assert.True(await _plane.SessionService.IsFamilyActiveAsync(blog.FamilyId));
        AuditEventDocument audit = Assert.Single(_plane.Events(AuditEventTypes.ApplicationBlocked));
        Assert.Equal("1", audit.Data!["SessionsEnded"]);
    }

    [Fact]
    public async Task ABlockedApplication_CannotBeForgotten_SoForgettingNeverLiftsABlock()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        await _plane.Apps.BlockAsync(_plane.Admin, "portal", null);

        AdminException refusal = await Assert.ThrowsAsync<AdminException>(() => _plane.Apps.ForgetAsync(_plane.Admin, "portal"));
        Assert.Equal(AdminErrorKinds.Invalid, refusal.Kind);

        await _plane.Apps.UnblockAsync(_plane.Admin, "portal");
        await _plane.Apps.ForgetAsync(_plane.Admin, "portal");

        Assert.Empty(_plane.Applications.Documents);
        Assert.True((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
        Assert.Single(_plane.Applications.Documents);
    }

    [Fact]
    public async Task AnApplicationNobodyHasSeen_CannotBeManaged()
    {
        AdminException refusal = await Assert.ThrowsAsync<AdminException>(() => _plane.Apps.BlockAsync(_plane.Admin, "never-seen", null));

        Assert.Equal(AdminErrorKinds.NotFound, refusal.Kind);
    }

    [Fact]
    public async Task TheList_ShowsEveryApplicationSeen_WithItsActiveSessions()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        await _plane.Directory.AuthenticatePublicAsync("https://site.example/callback");
        await _plane.SessionService.IssueFamilyAsync(Guid.NewGuid(), "portal", null, null, null);

        IReadOnlyList<ApplicationView> list = await _plane.Apps.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.Single(view => view.Document.ClientId == "portal").ActiveSessions);
    }
}
