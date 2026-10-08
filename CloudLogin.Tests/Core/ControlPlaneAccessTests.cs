using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// Who may administer what, and the account and session actions an administrator takes. The point of
/// the role split is that not every administrator is unrestricted, so these pin both what each role
/// is denied and that the realm cannot lock itself out.
/// </summary>
public class ControlPlaneAccessTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryCredentialRepository _credentials = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly InMemoryAuditEventRepository _auditEvents = new();
    private readonly CloudLoginCoreConfiguration _core = new();
    private readonly AuditLogger _audit;
    private readonly SessionService _sessionService;
    private readonly AdminAuthorizer _authorizer;
    private readonly AdministratorService _administrators;
    private readonly AdminUserService _userService;

    public ControlPlaneAccessTests()
    {
        _audit = new AuditLogger(_auditEvents, _core);
        _sessionService = new SessionService(_sessions, _core, _audit);
        _authorizer = new AdminAuthorizer(_users);
        _administrators = new AdministratorService(_users, _authorizer, _audit);
        _userService = new AdminUserService(_users, _credentials, _sessionService, _audit);
    }

    private async Task<Guid> AddUserAsync(
        AdminRoles[]? roles = null, bool global = false, bool locked = false, UserStates state = UserStates.Active, string name = "Ada")
    {
        Guid id = Guid.NewGuid();

        await _users.CreateAsync(new UserDocument
        {
            Id = id.ToString(),
            FirstName = name,
            LastName = "Tester",
            IsGlobalAdmin = global,
            IsLocked = locked,
            State = state,
            AdminRoles = [.. roles ?? []],
            Contacts = [new UserContact { Format = "EmailAddress", Value = $"{name.ToLowerInvariant()}@example.test", NormalizedValue = $"{name.ToLowerInvariant()}@example.test", IsPrimary = true }]
        });

        return id;
    }

    // ── Roles and permissions ───────────────────────────────────────────────

    [Fact]
    public void FullAdministrator_HoldsEveryPermission()
    {
        IReadOnlyCollection<string> permissions = AdminRolePolicy.PermissionsFor(AdminRoles.FullAdministrator);

        Assert.All(AdminPermissions.All, permission => Assert.Contains(permission, permissions));
    }

    [Fact]
    public void AuditReader_CanOnlyRead()
    {
        IReadOnlyCollection<string> permissions = AdminRolePolicy.PermissionsFor(AdminRoles.AuditReader);

        Assert.Contains(AdminPermissions.AuditRead, permissions);
        Assert.Contains(AdminPermissions.DashboardRead, permissions);
        Assert.DoesNotContain(permissions, permission => permission.EndsWith(".manage") || permission.EndsWith(".revoke"));
    }

    [Theory]
    [InlineData(AdminRoles.UserAdministrator, AdminPermissions.ApplicationsManage)]
    [InlineData(AdminRoles.UserAdministrator, AdminPermissions.KeysManage)]
    [InlineData(AdminRoles.ApplicationAdministrator, AdminPermissions.UsersManage)]
    [InlineData(AdminRoles.ApplicationAdministrator, AdminPermissions.KeysManage)]
    [InlineData(AdminRoles.ApplicationAdministrator, AdminPermissions.AdministratorsManage)]
    [InlineData(AdminRoles.SecurityAdministrator, AdminPermissions.ApplicationsManage)]
    [InlineData(AdminRoles.SecurityAdministrator, AdminPermissions.AdministratorsManage)]
    public void NarrowRoles_AreDeniedWhatTheyShouldNotHave(AdminRoles role, string permission) =>
        Assert.DoesNotContain(permission, AdminRolePolicy.PermissionsFor(role));

    [Fact]
    public void GlobalAdminFlag_ResolvesToEveryPermission_AndNoRolesToNone()
    {
        Assert.Equal(AdminPermissions.All.Count, AdminRolePolicy.Resolve(isGlobalAdmin: true, []).Count);
        Assert.Empty(AdminRolePolicy.Resolve(isGlobalAdmin: false, []));
    }

    [Fact]
    public async Task Authorizer_ReadsTheStoredAccountEachTime_SoARemovedRoleStopsWorkingAtOnce()
    {
        Guid id = await AddUserAsync([AdminRoles.ApplicationAdministrator]);

        await _authorizer.RequireAsync(id, AdminPermissions.ApplicationsManage);

        UserDocument user = (await _users.GetAsync(id))!;
        user.AdminRoles = [];
        await _users.ReplaceAsync(user);

        AdminException exception = await Assert.ThrowsAsync<AdminException>(() => _authorizer.RequireAsync(id, AdminPermissions.ApplicationsManage));
        Assert.Equal(AdminErrorKinds.Forbidden, exception.Kind);
    }

    [Theory]
    [InlineData(true, UserStates.Active)]
    [InlineData(false, UserStates.Disabled)]
    [InlineData(false, UserStates.Deleted)]
    public async Task LockedDisabledOrDeletedAccounts_HoldNoAdministrativePower(bool locked, UserStates state)
    {
        Guid id = await AddUserAsync([AdminRoles.FullAdministrator], global: true, locked: locked, state: state);

        Assert.False((await _authorizer.GetAccessAsync(id)).IsAdministrator);
    }

    [Fact]
    public async Task UnknownAccount_HoldsNothing() =>
        Assert.False((await _authorizer.GetAccessAsync(Guid.NewGuid())).IsAdministrator);

    // ── Administrators ──────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyAFullAdministrator_CanChangeRoles()
    {
        Guid actor = await AddUserAsync([AdminRoles.ApplicationAdministrator]);
        Guid target = await AddUserAsync(name: "Grace");

        AdminException exception = await Assert.ThrowsAsync<AdminException>(() =>
            _administrators.SetRolesAsync(actor, target, [AdminRoles.AuditReader]));

        Assert.Equal(AdminErrorKinds.Forbidden, exception.Kind);
    }

    [Fact]
    public async Task TheLastFullAdministrator_CannotBeDemoted()
    {
        Guid only = await AddUserAsync([AdminRoles.FullAdministrator]);

        AdminException exception = await Assert.ThrowsAsync<AdminException>(() =>
            _administrators.SetRolesAsync(only, only, [AdminRoles.AuditReader]));

        Assert.Equal(AdminErrorKinds.Conflict, exception.Kind);

        Guid second = await AddUserAsync([AdminRoles.FullAdministrator], name: "Grace");
        await _administrators.SetRolesAsync(only, second, [AdminRoles.AuditReader]);

        Assert.Contains(AdminRoles.AuditReader, (await _users.GetAsync(second))!.AdminRoles);
    }

    [Fact]
    public async Task RoleChanges_AreAudited_WithBeforeAndAfter()
    {
        Guid actor = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid target = await AddUserAsync(name: "Grace");

        await _administrators.SetRolesAsync(actor, target, [AdminRoles.SecurityAdministrator]);

        AuditEventDocument item = Assert.Single(_auditEvents.Events, entry => entry.EventType == AuditEventTypes.AdminRolesChanged);
        Assert.Equal(actor.ToString(), item.ActorUserId);
        Assert.Equal(target.ToString(), item.UserId);
        Assert.Equal("SecurityAdministrator", item.Data!["After"]);
    }

    [Fact]
    public async Task OnlyActiveAccounts_CanBecomeAdministrators()
    {
        Guid actor = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid disabled = await AddUserAsync(state: UserStates.Disabled, name: "Grace");

        await Assert.ThrowsAsync<AdminException>(() => _administrators.SetRolesAsync(actor, disabled, [AdminRoles.AuditReader]));
    }

    // ── Users ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisablingAnAccount_EndsItsSessionsAndRotatesTheSecurityStamp()
    {
        Guid admin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid target = await AddUserAsync(name: "Grace");
        SessionIssueResult session = await _sessionService.IssueFamilyAsync(target, audience: SessionService.BrowserAudience);
        string stampBefore = (await _users.GetAsync(target))!.SecurityStamp;

        AdminUserSummary summary = await _userService.SetDisabledAsync(admin, target, disabled: true);

        Assert.Equal(UserStates.Disabled, summary.State);
        Assert.False(await _sessionService.IsFamilyActiveAsync(session.FamilyId));
        Assert.NotEqual(stampBefore, (await _users.GetAsync(target))!.SecurityStamp);
        Assert.Single(_auditEvents.Events, entry => entry.EventType == AuditEventTypes.UserDisabled && entry.ActorUserId == admin.ToString());
        Assert.False((await _authorizer.GetAccessAsync(target)).IsAdministrator);
    }

    [Fact]
    public async Task EnablingAnAccount_RestoresSignInButNotTheSessionsThatWereEnded()
    {
        Guid admin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid target = await AddUserAsync(name: "Grace");
        SessionIssueResult session = await _sessionService.IssueFamilyAsync(target, audience: SessionService.BrowserAudience);

        await _userService.SetDisabledAsync(admin, target, disabled: true);
        AdminUserSummary summary = await _userService.SetDisabledAsync(admin, target, disabled: false);

        Assert.Equal(UserStates.Active, summary.State);
        Assert.False(await _sessionService.IsFamilyActiveAsync(session.FamilyId));
    }

    [Fact]
    public async Task AnAdministrator_CannotDisableThemselves_OrTheLastFullAdministrator()
    {
        Guid onlyAdmin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid userAdmin = await AddUserAsync([AdminRoles.UserAdministrator], name: "Grace");

        await Assert.ThrowsAsync<AdminException>(() => _userService.SetDisabledAsync(onlyAdmin, onlyAdmin, disabled: true));

        AdminException exception = await Assert.ThrowsAsync<AdminException>(() => _userService.SetDisabledAsync(userAdmin, onlyAdmin, disabled: true));
        Assert.Equal(AdminErrorKinds.Conflict, exception.Kind);
    }

    [Fact]
    public async Task UserDetail_ShowsHowTheyAuthenticateAndWhichProvidersAreLinked_WithoutSecrets()
    {
        Guid target = await AddUserAsync(name: "Grace");

        await _credentials.CreateAsync(new CredentialDocument
        {
            Id = "ext-google",
            UserId = target.ToString(),
            Kind = CredentialKinds.ExternalIdentity,
            ProviderCode = "Google",
            ProviderEmail = "grace@gmail.test",
            CreatedOn = DateTimeOffset.UtcNow
        });

        await _credentials.CreateAsync(new CredentialDocument
        {
            Id = "pk-1",
            UserId = target.ToString(),
            Kind = CredentialKinds.Passkey,
            PasskeyName = "Laptop",
            CreatedOn = DateTimeOffset.UtcNow
        });

        AdminUserDetail detail = await _userService.GetDetailAsync(target);

        Assert.Equal("Google", Assert.Single(detail.LinkedIdentities).ProviderCode);
        Assert.Equal("Laptop", Assert.Single(detail.Passkeys).Name);
        Assert.False(detail.HasPassword);
        Assert.DoesNotContain("PasswordHash", System.Text.Json.JsonSerializer.Serialize(detail));
    }

    [Fact]
    public async Task RevokingAPasskey_RemovesItAndSignsTheAccountOut()
    {
        Guid admin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid target = await AddUserAsync(name: "Grace");
        SessionIssueResult session = await _sessionService.IssueFamilyAsync(target, audience: SessionService.BrowserAudience);

        await _credentials.CreateAsync(new CredentialDocument
        {
            Id = "pk-1", UserId = target.ToString(), Kind = CredentialKinds.Passkey, PasskeyName = "Lost phone", CreatedOn = DateTimeOffset.UtcNow
        });

        await _userService.RevokePasskeyAsync(admin, target, "pk-1");

        Assert.Empty((await _userService.GetDetailAsync(target)).Passkeys);
        Assert.False(await _sessionService.IsFamilyActiveAsync(session.FamilyId));
        Assert.Single(_auditEvents.Events, entry => entry.EventType == AuditEventTypes.DeviceRevoked);

        await Assert.ThrowsAsync<AdminException>(() => _userService.RevokePasskeyAsync(admin, target, "pk-1"));
    }

    [Fact]
    public async Task UserSearch_FindsByNameOrContact()
    {
        await AddUserAsync(name: "Ada");
        await AddUserAsync(name: "Grace");

        Assert.Single(await _userService.SearchAsync("grace", 0, 25));
        Assert.Single(await _userService.SearchAsync("ada@example.test", 0, 25));
        Assert.Equal(2, (await _userService.SearchAsync(null, 0, 25)).Count);
    }

    // ── Sessions ────────────────────────────────────────────────────────────

    private (AdminSessionService Sessions, ControlPlaneFixture Plane) SessionsWithApps()
    {
        ControlPlaneFixture plane = new();
        ApplicationService applications = new(plane.Applications, _sessions, _sessionService, plane.Directory, _audit);
        AdminSessionService sessions = new(_sessions, _sessionService, _users, applications, _audit);
        return (sessions, plane);
    }

    [Fact]
    public async Task AdministratorCanListAndRevokeASession_AndItIsAudited()
    {
        (AdminSessionService sessions, ControlPlaneFixture plane) = SessionsWithApps();
        await plane.AuthenticateAsync("portal-app", ControlPlaneFixture.DeploymentKey);
        Guid admin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid user = await AddUserAsync(name: "Grace");
        SessionIssueResult issued = await _sessionService.IssueFamilyAsync(user, audience: "portal-app");

        IReadOnlyList<AdminSessionView> listed = await sessions.ListActiveAsync("portal-app", null, 50);
        Assert.Equal("portal-app", Assert.Single(listed).ApplicationClientId);

        await sessions.RevokeAsync(admin, issued.FamilyId);

        Assert.False(await _sessionService.IsFamilyActiveAsync(issued.FamilyId));
        Assert.Empty(await sessions.ListActiveAsync("portal-app", null, 50));
        Assert.Single(_auditEvents.Events, entry => entry.EventType == AuditEventTypes.SessionRevoked && entry.ActorUserId == admin.ToString());
    }

    [Fact]
    public async Task RevokingForAnApplication_EndsOnlyThatApplicationsSessions()
    {
        (AdminSessionService sessions, ControlPlaneFixture plane) = SessionsWithApps();
        await plane.AuthenticateAsync("portal-app", ControlPlaneFixture.DeploymentKey);
        await plane.AuthenticateAsync("other-app", ControlPlaneFixture.DeploymentKey);
        Guid admin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid user = await AddUserAsync(name: "Grace");

        SessionIssueResult one = await _sessionService.IssueFamilyAsync(user, audience: "portal-app");
        SessionIssueResult two = await _sessionService.IssueFamilyAsync(user, audience: "portal-app");
        SessionIssueResult safe = await _sessionService.IssueFamilyAsync(user, audience: "other-app");

        int revoked = await sessions.RevokeForApplicationAsync(admin, "portal-app");

        Assert.Equal(2, revoked);
        Assert.False(await _sessionService.IsFamilyActiveAsync(one.FamilyId));
        Assert.False(await _sessionService.IsFamilyActiveAsync(two.FamilyId));
        Assert.True(await _sessionService.IsFamilyActiveAsync(safe.FamilyId));
    }

    [Fact]
    public async Task SignOutEverywhere_EndsEverySession_AndRotatesTheStamp()
    {
        (AdminSessionService sessions, _) = SessionsWithApps();
        Guid admin = await AddUserAsync([AdminRoles.FullAdministrator]);
        Guid user = await AddUserAsync(name: "Grace");
        SessionIssueResult browser = await _sessionService.IssueFamilyAsync(user, audience: SessionService.BrowserAudience);
        SessionIssueResult app = await _sessionService.IssueFamilyAsync(user, audience: "portal-app");
        string stamp = (await _users.GetAsync(user))!.SecurityStamp;

        await sessions.SignOutEverywhereAsync(admin, user);

        Assert.False(await _sessionService.IsFamilyActiveAsync(browser.FamilyId));
        Assert.False(await _sessionService.IsFamilyActiveAsync(app.FamilyId));
        Assert.NotEqual(stamp, (await _users.GetAsync(user))!.SecurityStamp);
        Assert.Single(_auditEvents.Events, entry => entry.EventType == AuditEventTypes.UserSignedOutEverywhere);
    }

    // ── Dashboard ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_ReportsTheRealmFromStoredState()
    {
        ControlPlaneFixture plane = new();
        AdminDashboardService dashboard = new(_users, _sessions, plane.Apps, plane.Keys, _auditEvents);

        await AddUserAsync(name: "Ada");
        Guid user = await AddUserAsync(name: "Grace");
        await _sessionService.IssueFamilyAsync(user, audience: SessionService.BrowserAudience);
        await plane.AuthenticateAsync("active-app", ControlPlaneFixture.DeploymentKey);
        await plane.AuthenticateAsync("off-app", ControlPlaneFixture.DeploymentKey);
        await plane.Apps.BlockAsync(plane.Admin, "off-app", null);
        await _audit.LogAsync(new AuditEntry { EventType = AuditEventTypes.ClientAuthenticationFailed, ClientId = "active-app", Result = AuditResults.Denied });

        DashboardSummary summary = await dashboard.GetAsync();

        Assert.Equal(2, summary.Users);
        Assert.Equal(1, summary.ActiveSessions);
        Assert.Equal(2, summary.Applications);
        Assert.Equal(1, summary.BlockedApplications);
        Assert.Equal(0, summary.ExpiringSecretKeys);
        Assert.Equal(1, summary.FailedAuthenticationsLast24Hours);
        Assert.NotEmpty(summary.RecentSecurityEvents);
    }
}
