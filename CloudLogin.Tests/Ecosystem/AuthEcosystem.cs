using System.Security.Claims;
using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Controllers;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using AngryMonkey.CloudLogin.Tests.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

internal sealed class EcosystemTokenStore(InMemorySessionRepository sessions, CloudLoginCoreConfiguration core, ILogoutNotifier? notifier) : ICloudLoginTokenStore, IAtomicCloudLoginTokenStore, ICloudLoginSessionOwnerLookup
{
    private readonly CoreTokenStoreAdapter _adapter = new(sessions, null!, core, notifier);
    private readonly List<CloudLoginSigningKey> _keys = [];

    public Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudLoginSigningKey>>([.. _keys]);

    public Task SaveSigningKeyAsync(CloudLoginSigningKey key, CancellationToken cancellationToken = default)
    {
        _keys.RemoveAll(existing => existing.KeyId == key.KeyId);
        _keys.Add(key);
        return Task.CompletedTask;
    }

    public Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default) => _adapter.FindRefreshTokenAsync(tokenHash, cancellationToken);

    public Task SaveRefreshTokenAsync(CloudLoginRefreshToken token, CancellationToken cancellationToken = default) => _adapter.SaveRefreshTokenAsync(token, cancellationToken);

    public Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default) => _adapter.RevokeFamilyAsync(familyId, cancellationToken);

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default) => _adapter.RevokeSessionAsync(sessionId, cancellationToken);

    public Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default) => _adapter.RevokeUserAsync(userId, cancellationToken);

    public Task<CloudLoginRefreshRotationResult> RotateRefreshTokenAsync(CloudLoginRefreshToken current, CloudLoginRefreshToken replacement, CancellationToken cancellationToken = default) =>
        _adapter.RotateRefreshTokenAsync(current, replacement, cancellationToken);

    public Task<IReadOnlyCollection<Guid>> GetSessionOwnersAsync(string sessionId, CancellationToken cancellationToken = default) => _adapter.GetSessionOwnersAsync(sessionId, cancellationToken);

    public Task<bool> IsSessionActiveAsync(string sessionId, CancellationToken cancellationToken = default) => _adapter.IsSessionActiveAsync(sessionId, cancellationToken);
}

internal sealed class AuthorityHost : IAsyncDisposable
{
    public const string Origin = "https://login.test";

    public required WebApplication App { get; init; }
    public required InMemoryCloudLoginStore Store { get; init; }
    public required InMemorySessionRepository Sessions { get; init; }
    public required StoreBackedLoginRequestRepository LoginRequests { get; init; }
    public required InMemoryApplicationRepository Applications { get; init; }
    public required InMemorySecretKeyRepository SecretKeyStore { get; init; }
    public required InMemoryAuditEventRepository Audit { get; init; }
    public required CloudLoginTokenOptions TokenOptions { get; init; }

    public TestServer Server => App.GetTestServer();

    public IServiceProvider Services => App.Services;

    public SessionService SessionService => Services.GetRequiredService<SessionService>();

    public BackChannelLogoutService BackChannel => Services.GetRequiredService<BackChannelLogoutService>();

    public ApplicationService Admin => Services.GetRequiredService<ApplicationService>();

    public SecretKeyService Keys => Services.GetRequiredService<SecretKeyService>();

    public IClientDirectory Directory => Services.GetRequiredService<IClientDirectory>();

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}

internal sealed class ConsumerHost(string name, string origin, int instances, string secret) : IAsyncDisposable
{
    public string Name { get; } = name;
    public string Origin { get; } = origin;
    public string Secret { get; } = secret;
    public string Host { get; } = new Uri(origin).Host;
    public FaultableCache Cache { get; } = new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
    public List<WebApplication> Instances { get; } = [];
    public int InstanceCount { get; } = instances;
    public InstanceBalancer Balancer { get; set; } = null!;

    public async ValueTask DisposeAsync()
    {
        foreach (WebApplication app in Instances)
            await app.DisposeAsync();
    }
}

internal sealed class AuthEcosystem : IAsyncDisposable
{
    public const string AuthorityEmail = "person@example.com";
    public const string Password = "Valid#123456";

    private int _next;

    public RoutingHandler Network { get; } = new();
    public AuthorityHost Authority { get; private set; } = null!;
    public List<ConsumerHost> Consumers { get; } = [];
    public CloudUser User { get; private set; } = null!;

    public static async Task<AuthEcosystem> CreateAsync(Action<CloudLoginTokenOptions>? configureAuthority = null)
    {
        AuthEcosystem ecosystem = new();
        await ecosystem.StartAuthorityAsync(configureAuthority);
        return ecosystem;
    }

    public TestBrowser NewBrowser() => new(Network);

    /// <summary>Blocks an application at the authority, recording it first if it has not signed anyone in yet.</summary>
    public async Task BlockAsync(ConsumerHost app)
    {
        await Authority.Directory.AuthenticateAsync(app.Name, app.Secret);
        await Authority.Admin.BlockAsync(Guid.NewGuid(), app.Name, null);
    }

    private async Task StartAuthorityAsync(Action<CloudLoginTokenOptions>? configure)
    {
        InMemoryCloudLoginStore store = new();
        InMemorySessionRepository sessions = new();
        StoreBackedLoginRequestRepository loginRequests = new(store);
        InMemoryApplicationRepository applications = new();
        InMemorySecretKeyRepository secretKeys = new();
        InMemoryAuditEventRepository audit = new();
        CloudLoginCoreConfiguration core = new();

        CloudLoginTokenOptions tokenOptions = new()
        {
            Issuer = AuthorityHost.Origin,
            AccessTokenLifetime = TimeSpan.FromMinutes(10),
            RefreshTokenLifetime = TimeSpan.FromDays(14),
            SigningKeyPublishGrace = TimeSpan.FromHours(2)
        };

        configure?.Invoke(tokenOptions);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        CloudLoginWebConfiguration configuration = new() { BaseAddress = AuthorityHost.Origin, WebConfig = static _ => { }, LoginDuration = TimeSpan.FromDays(14) };

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(core);
        builder.Services.AddSingleton<ICloudLoginStore>(store);
        builder.Services.AddSingleton<ICloudLoginSecurityStore>(new InMemorySecurityStore());
        builder.Services.AddSingleton<ISessionRepository>(sessions);
        builder.Services.AddSingleton<ILoginRequestRepository>(loginRequests);
        builder.Services.AddSingleton<IApplicationRepository>(applications);
        builder.Services.AddSingleton<ISecretKeyRepository>(secretKeys);
        builder.Services.AddSingleton<IAuditEventRepository>(audit);
        builder.Services.AddSingleton<IAuditLogger>(provider => new AuditLogger(audit, core));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDataProtection();
        builder.Services.AddSingleton<AuthorizationTransactionService>();
        builder.Services.AddSingleton<ApplicationService>();
        builder.Services.AddSingleton<SecretKeyService>();
        builder.Services.AddSingleton<CloudLoginAuthenticationService>();
        builder.Services.AddSingleton<ICloudLoginTokenStore>(provider => new EcosystemTokenStore(sessions, core, provider.GetRequiredService<ILogoutNotifier>()));
        builder.Services.AddSingleton<SessionService>(provider => new SessionService(sessions, core, provider.GetRequiredService<IAuditLogger>(), provider.GetRequiredService<ILogoutNotifier>()));
        builder.Services.AddScoped<CloudLoginServer>(provider => new CloudLoginServer(
            new CloudGeographyClient(),
            configuration,
            provider.GetRequiredService<IHttpContextAccessor>(),
            cloudLoginStore: store,
            securityStore: provider.GetRequiredService<ICloudLoginSecurityStore>(),
            sessionService: provider.GetRequiredService<SessionService>(),
            verificationStore: new AngryMonkey.CloudLogin.Server.Verification.InMemoryVerificationStore()));
        builder.Services.AddScoped<ICloudLogin>(provider => provider.GetRequiredService<CloudLoginServer>());

        builder.Services.AddCloudLoginTokenIssuer(options =>
        {
            options.Issuer = tokenOptions.Issuer;
            options.AccessTokenLifetime = tokenOptions.AccessTokenLifetime;
            options.RefreshTokenLifetime = tokenOptions.RefreshTokenLifetime;
            options.SigningKeyPublishGrace = tokenOptions.SigningKeyPublishGrace;
            options.SecretKeys = tokenOptions.SecretKeys;
            options.AllowUnboundRefreshTokens = tokenOptions.AllowUnboundRefreshTokens;
            options.AllowDeploymentKeysWithoutRegistry = tokenOptions.AllowDeploymentKeysWithoutRegistry;
            options.RegistryStaleTolerance = tokenOptions.RegistryStaleTolerance;
        });

        builder.Services.AddCloudLoginBackChannelLogout();
        builder.Services.Configure<BackChannelLogoutOptions>(options =>
        {
            options.DeliverInline = true;
            options.RetryDelays = [TimeSpan.FromMilliseconds(5)];
            options.AttemptTimeout = TimeSpan.FromSeconds(5);
        });

        builder.Services.AddHttpClient(BackChannelLogoutDefaults.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Network);

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "__Host-CloudLogin";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.HttpOnly = true;
            options.Events = new CookieAuthenticationEvents
            {
                OnSigningIn = async context =>
                {
                    CloudLoginAuthenticationService service = context.HttpContext.RequestServices.GetRequiredService<CloudLoginAuthenticationService>();
                    context.Principal = await service.HandleSignIn(context.Principal!, context.HttpContext);
                }
            };
        });

        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TokenController).Assembly)
            .AddApplicationPart(typeof(AngryMonkey.CloudLogin.API.Controllers.LoginController).Assembly);

        WebApplication app = builder.Build();
        store.CurrentSessionId = () => app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User.FindFirst(CloudLoginClaims.SessionId)?.Value;
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        Authority = new AuthorityHost
        {
            App = app,
            Store = store,
            Sessions = sessions,
            LoginRequests = loginRequests,
            Applications = applications,
            SecretKeyStore = secretKeys,
            Audit = audit,
            TokenOptions = tokenOptions
        };

        Network.Routes[new Uri(AuthorityHost.Origin).Host] = app.GetTestServer().CreateHandler();

        CloudUser user = LoginTestFixture.CreateUser(AuthorityEmail);
        user.Inputs[0].Providers.Add(new CloudLoginProvider { Code = "Password", PasswordHash = await app.Services.CreateScope().ServiceProvider.GetRequiredService<CloudLoginServer>().HashPassword(Password) });
        store.Users[user.Id] = user;
        User = user;
    }

    public CloudUser AddUser(string email)
    {
        CloudUser user = LoginTestFixture.CreateUser(email);
        user.Inputs[0].Providers.Add(new CloudLoginProvider { Code = "Password", PasswordHash = Authority.Services.CreateScope().ServiceProvider.GetRequiredService<CloudLoginServer>().HashPassword(Password).GetAwaiter().GetResult() });
        Authority.Store.Users[user.Id] = user;
        return user;
    }

    /// <summary>Declares the application's secret key to the authority, the way an AppHost reference does, and starts the application hosts for it.</summary>
    public async Task<ConsumerHost> AddConsumerAsync(
        string name,
        int instances = 1,
        string? secret = null,
        Action<Dictionary<string, string?>>? configureConsumer = null,
        string? publicUrl = null,
        Action<CloudLoginServerConfiguration>? configureServer = null)
    {
        string origin = $"https://{name}.test";
        secret ??= $"secret-for-{name}-{Guid.NewGuid():N}";

        // Any website with a backend authenticates with any deployment key, so declaring this one is all the authority needs.
        if (!Authority.TokenOptions.SecretKeys.Contains(secret))
            Authority.TokenOptions.SecretKeys.Add(secret);
        Authority.Directory.Invalidate();

        ConsumerHost consumer = new(name, origin, instances, secret);

        for (int index = 0; index < instances; index++)
            consumer.Instances.Add(await StartConsumerInstanceAsync(consumer, secret, publicUrl ?? origin, configureConsumer, configureServer));

        consumer.Balancer = new InstanceBalancer(consumer.Instances.Select(app => app.GetTestServer().CreateHandler()).ToList());
        Network.Routes[consumer.Host] = consumer.Balancer;
        Consumers.Add(consumer);
        _ = _next;
        return consumer;
    }

    private async Task<WebApplication> StartConsumerInstanceAsync(ConsumerHost consumer, string secret, string publicUrl, Action<Dictionary<string, string?>>? configureConsumer, Action<CloudLoginServerConfiguration>? configureServer)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        Dictionary<string, string?> settings = new()
        {
            ["LoginUrl"] = AuthorityHost.Origin,
            ["CloudLogin:Authority"] = AuthorityHost.Origin,
            ["CloudLogin:Audience"] = consumer.Name,
            ["CloudLogin:ClientId"] = consumer.Name,
            ["CloudLogin:ClientSecret"] = secret,
            ["CloudLogin:PublicUrl"] = publicUrl,
            ["CloudLogin:SessionRevalidationInterval"] = "00:00:00"
        };

        configureConsumer?.Invoke(settings);
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddCloudLoginServer(AuthorityHost.Origin, configuration =>
        {
            configuration.CookieName = $"{consumer.Name}.session";
            configuration.RequireHttps = false;
            configureServer?.Invoke(configuration);
        });

        builder.Services.AddSingleton<IDistributedCache>(consumer.Cache);
        builder.Services.AddSingleton<IDataProtectionProvider>(SharedProtection(consumer.Name));
        builder.Services.AddHttpClient(CloudLoginTokenClientOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Network);
        builder.Services.AddHttpClient().ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => Network));

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        app.MapGet("/me", async (HttpContext context) =>
        {
            AuthenticateResult result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return result.Succeeded
                ? Results.Json(new { authenticated = true, sid = result.Principal!.FindFirst(CloudLoginSessionClaims.SessionId)?.Value, user = result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value })
                : Results.Json(new { authenticated = false }, statusCode: StatusCodes.Status401Unauthorized);
        });

        await app.StartAsync();
        return app;
    }

    private readonly Dictionary<string, IDataProtectionProvider> _protection = [];

    private IDataProtectionProvider SharedProtection(string name)
    {
        if (!_protection.TryGetValue(name, out IDataProtectionProvider? provider))
            _protection[name] = provider = new EphemeralDataProtectionProvider();

        return provider;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ConsumerHost consumer in Consumers)
            await consumer.DisposeAsync();

        await Authority.DisposeAsync();
    }
}

internal sealed class InstanceBalancer(IReadOnlyList<HttpMessageHandler> handlers) : HttpMessageHandler
{
    private int _next;

    public int Instance { get; set; } = -1;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int index = Instance >= 0 ? Instance : Interlocked.Increment(ref _next) % handlers.Count;
        return new HttpMessageInvoker(handlers[index], disposeHandler: false).SendAsync(request, cancellationToken);
    }
}
