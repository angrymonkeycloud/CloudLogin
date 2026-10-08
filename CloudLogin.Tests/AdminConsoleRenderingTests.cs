using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// The administration console renders from what the admin API returns, shows only the tabs the
/// signed-in administrator's permissions allow, and never shows a secret key except the one just created.
/// The API is stubbed, so this checks the console, not the server behind it.
/// </summary>
public class AdminConsoleRenderingTests
{
    private sealed class StubApi(Dictionary<string, (HttpStatusCode Status, object Body)> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery;
            (HttpStatusCode Status, object Body)? match = routes
                .Where(route => path.StartsWith(route.Key, StringComparison.Ordinal))
                .OrderByDescending(route => route.Key.Length)
                .Select(route => ((HttpStatusCode, object)?)route.Value)
                .FirstOrDefault();

            HttpResponseMessage response = match is { } found
                ? new HttpResponseMessage(found.Status) { Content = JsonContent.Create(found.Body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }

    private static readonly string[] AllPermissions =
    [
        "dashboard.read", "users.read", "users.manage", "sessions.read", "sessions.revoke", "applications.read", "applications.manage",
        "secretkeys.manage", "secretkeys.revoke", "keys.read", "keys.manage", "providers.read",
        "audit.read", "administrators.read", "administrators.manage"
    ];

    private static V3AdminMeResponse Me(bool global, params string[] permissions) => new()
    {
        UserId = Guid.NewGuid(),
        DisplayName = "Ada",
        IsGlobalAdmin = global,
        Permissions = [.. permissions]
    };

    private static V3AdminApplicationResponse App(string clientId = "portal-app", string kind = "Backend", bool blocked = false) => new()
    {
        ClientId = clientId,
        Kind = kind,
        Origins = kind == "Backend" ? ["https://portal.example"] : [clientId],
        BackChannelLogoutUri = kind == "Backend" ? "https://portal.example/auth/backchannel-logout" : null,
        FirstSeenOn = DateTimeOffset.UtcNow.AddDays(-10),
        LastSeenOn = DateTimeOffset.UtcNow,
        ActiveSessions = 3,
        IsBlocked = blocked,
        BlockedOn = blocked ? DateTimeOffset.UtcNow : null,
        BlockReason = blocked ? "compromised" : null
    };

    private static async Task<string> RenderAsync<TComponent>(
        Dictionary<string, (HttpStatusCode, object)> routes, Dictionary<string, object?>? parameters = null) where TComponent : IComponent
    {
        CloudLoginClient client = new() { HttpServer = new HttpClient(new StubApi(routes)) { BaseAddress = new Uri("https://login.example/") } };

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ICloudLogin>(client);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters ?? []))).ToHtmlString());
    }

    private static Dictionary<string, (HttpStatusCode, object)> OverviewRoutes(V3AdminMeResponse me) => new()
    {
        ["/api/v3/admin/me"] = (HttpStatusCode.OK, me),
        ["/api/v3/admin/dashboard"] = (HttpStatusCode.OK, new V3AdminDashboardResponse
        {
            Users = 42,
            ActiveSessions = 17,
            ActiveApplications = 5,
            BlockedApplications = 1,
            ExpiringKeys = 2,
            FailedAuthenticationsLast24Hours = 9,
            RecentEvents = [new V3AdminAuditEventModel { EventId = "e1", EventType = "Application.Created", OccurredOn = DateTimeOffset.UtcNow, ClientId = "portal-app", Result = "Success" }]
        }),
        ["/api/v3/admin/secret-keys/expiring"] = (HttpStatusCode.OK, new List<V3AdminSecretKeyModel>
        {
            new() { Id = "key-1", Label = "Partner websites", Prefix = "clsk_AbCd", Status = "Active", ExpiresOn = DateTimeOffset.UtcNow.AddDays(3) }
        })
    };

    [Fact]
    public async Task AFullAdministrator_SeesEveryTab_AndTheDashboard()
    {
        V3AdminMeResponse me = Me(global: true, AllPermissions);

        string html = await RenderAsync<AdminComponent>(OverviewRoutes(me));

        foreach (string tab in new[] { "Overview", "Users", "Applications", "Secret keys", "Sessions", "Security", "Providers", "Audit", "Administrators" })
            Assert.Contains($">{tab}</button>", html);

        Assert.Contains("Global Admin", html);
        Assert.Contains(">42<", html);
        Assert.Contains(">17<", html);
        Assert.Contains("Application.Created", html);
        Assert.Contains("Secret keys that need attention", html);
    }

    [Fact]
    public async Task AnAuditReader_SeesOnlyTheTabsItsRoleAllows()
    {
        V3AdminMeResponse me = Me(global: false, "dashboard.read", "audit.read");

        string html = await RenderAsync<AdminComponent>(OverviewRoutes(me));

        Assert.Contains(">Overview</button>", html);
        Assert.Contains(">Audit</button>", html);
        Assert.DoesNotContain(">Applications</button>", html);
        Assert.DoesNotContain(">Users</button>", html);
        Assert.DoesNotContain(">Administrators</button>", html);
        Assert.DoesNotContain("Secret keys that need attention", html);
    }

    [Fact]
    public async Task ANonAdministrator_IsToldSo_AndSeesNothingElse()
    {
        Dictionary<string, (HttpStatusCode, object)> routes = new()
        {
            ["/api/v3/admin/me"] = (HttpStatusCode.Forbidden, new { title = "Not permitted", detail = "This account is not a CloudLogin administrator." })
        };

        string html = await RenderAsync<AdminComponent>(routes);

        Assert.Contains("not a CloudLogin administrator", html);
        Assert.DoesNotContain("admin-tabs", html);
    }

    [Fact]
    public async Task TheApplicationList_ShowsWhatWasSeen_AndOffersNothingToCreate()
    {
        Dictionary<string, (HttpStatusCode, object)> routes = new()
        {
            ["/api/v3/admin/applications"] = (HttpStatusCode.OK, new List<V3AdminApplicationResponse> { App(), App("https://site.example", "Website", blocked: true) })
        };

        string html = await RenderAsync<AdminComponent_Applications>(routes, new() { ["Access"] = Me(true, AllPermissions) });

        Assert.Contains("portal-app", html);
        Assert.Contains("https://portal.example", html);
        Assert.Contains("Website without a backend", html);
        Assert.Contains(">Blocked<", html);
        Assert.DoesNotContain("Connect an application", html);
        Assert.DoesNotContain("Register", html);
    }

    [Fact]
    public async Task ApplicationDetail_OffersBlockingOnlyToThoseAllowed()
    {
        Dictionary<string, (HttpStatusCode, object)> routes = new()
        {
            ["/api/v3/admin/applications/portal-app/sessions"] = (HttpStatusCode.OK, new List<V3AdminSessionResponse>())
        };

        string manager = await RenderAsync<AdminComponent_ApplicationDetail>(routes, new() { ["Application"] = App(), ["Access"] = Me(true, AllPermissions) });
        string reader = await RenderAsync<AdminComponent_ApplicationDetail>(routes, new() { ["Application"] = App(), ["Access"] = Me(false, "applications.read") });

        Assert.Contains("Block access", manager);
        Assert.Contains("https://portal.example/auth/backchannel-logout", manager);
        Assert.DoesNotContain("Block access", reader);
        Assert.DoesNotContain(">Forget<", reader);
    }

    [Fact]
    public async Task ABlockedApplication_ShowsWhyAndCanBeUnblocked_ButNotForgotten()
    {
        string html = await RenderAsync<AdminComponent_ApplicationDetail>([], new() { ["Application"] = App(blocked: true), ["Access"] = Me(true, AllPermissions) });

        Assert.Contains("compromised", html);
        Assert.Contains(">Unblock<", html);
        Assert.DoesNotContain(">Forget<", html);
    }

    [Fact]
    public async Task TheSecretKeyList_ShowsNoSecret_AndOffersCreationOnlyToThoseAllowed()
    {
        Dictionary<string, (HttpStatusCode, object)> routes = new()
        {
            ["/api/v3/admin/secret-keys"] = (HttpStatusCode.OK, new List<V3AdminSecretKeyModel>
            {
                new() { Id = "key-1", Label = "Partner websites", Prefix = "clsk_AbCd", Status = "Active", CreatedOn = DateTimeOffset.UtcNow, LastUsedBy = "portal-app", LastUsedOn = DateTimeOffset.UtcNow },
                new() { Id = "deployment-0123456789ab", Label = "Declared by the deployment", IsDeployment = true, Status = "Active" }
            })
        };

        string manager = await RenderAsync<AdminComponent_SecretKeys>(routes, new() { ["Access"] = Me(true, AllPermissions) });
        string reader = await RenderAsync<AdminComponent_SecretKeys>(routes, new() { ["Access"] = Me(false, "applications.read") });

        Assert.Contains("Partner websites", manager);
        Assert.Contains("clsk_AbCd...", manager);
        Assert.Contains("By the deployment", manager);
        Assert.Contains("Never", manager);
        Assert.Contains("Create a secret key", manager);
        Assert.Contains(">Revoke<", manager);
        Assert.DoesNotContain("Create a secret key", reader);
        Assert.DoesNotContain(">Revoke<", reader);
    }

    [Fact]
    public async Task TheSecurityTab_ShowsKeyStatus_AndExplainsKeyVaultWhenKeysLiveThere()
    {
        Dictionary<string, (HttpStatusCode, object)> local = new()
        {
            ["/api/v3/admin/keys"] = (HttpStatusCode.OK, new V3AdminKeysResponse
            {
                Keys = [new V3AdminKeyModel { KeyId = "kid-1", Status = "Active", CreatedOn = DateTimeOffset.UtcNow, SigningExpiresOn = DateTimeOffset.UtcNow.AddDays(3), PublishExpiresOn = DateTimeOffset.UtcNow.AddDays(6), ExpiresSoon = true }]
            }),
            ["/api/v3/admin/secret-keys/expiring"] = (HttpStatusCode.OK, new List<V3AdminSecretKeyModel>())
        };

        Dictionary<string, (HttpStatusCode, object)> vault = new()
        {
            ["/api/v3/admin/keys"] = (HttpStatusCode.OK, new V3AdminKeysResponse { ManagedInKeyVault = true }),
            ["/api/v3/admin/secret-keys/expiring"] = (HttpStatusCode.OK, new List<V3AdminSecretKeyModel>())
        };

        string localHtml = await RenderAsync<AdminComponent_Security>(local, new() { ["Access"] = Me(true, AllPermissions) });
        string vaultHtml = await RenderAsync<AdminComponent_Security>(vault, new() { ["Access"] = Me(true, AllPermissions) });

        Assert.Contains("kid-1", localHtml);
        Assert.Contains("Expires soon", localHtml);
        Assert.Contains("Rotate signing key now", localHtml);
        Assert.Contains("held in Key Vault", vaultHtml);
        Assert.DoesNotContain("Rotate signing key now", vaultHtml);
    }
}
