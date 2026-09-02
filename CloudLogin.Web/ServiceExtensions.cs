using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.API.Controllers;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Serialization;
using AngryMonkey.CloudLogin.Server.Storage;
using AngryMonkey.CloudLogin.Sever.Providers;
using AngryMonkey.CloudBlazor.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static void AddCloudLoginWeb(this IHostApplicationBuilder builder, CloudLoginWebConfiguration loginConfig)
    {
        CloudLoginConfigurationValidator.Validate(loginConfig, builder.Environment.IsDevelopment());

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();

        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
            options.Preload = true;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ProvidersController).Assembly);

        //CloudWebConfig? webConfig = builderConfiguration.Get<CloudWebConfig>();

        //services.AddAuthentication(options =>
        //{
        //    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        //    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        //});

        builder.Services.AddOptions();
        builder.Services.AddAuthenticationCore();
        builder.Services.AddScoped<CustomAuthenticationStateProvider>();
        builder.Services.AddScoped<ProviderConfigurationService>();

        builder.Services.TryAddScoped<CloudGeographyClient>();

        ConfigureCosmos(builder, loginConfig);
        ConfigureCloudWeb(builder.Services, loginConfig);
        ConfigureAuthentication(builder.Services, loginConfig);
        ConfigureSecurity(builder.Services, loginConfig);

        builder.Services.AddCloudLoginWeb(loginConfig);

        // CloudLogin's single seven-container storage core.
        builder.Services.AddCloudLoginCore(loginConfig);

        // CloudLogin owns its schema and creates its database and containers itself.
        builder.Services.AddCloudLoginStorageProvisioning();

        IConfigurationSection tokenConfiguration = builder.Configuration.GetSection("CloudLoginTokens");
        if (!string.IsNullOrWhiteSpace(tokenConfiguration["Issuer"]))
            builder.Services.AddCloudLoginTokenIssuer(tokenConfiguration);

        // Key Vault is the recommendation for production signing keys, but it is not demanded:
        // the Cosmos fallback is Data Protection-wrapped and TTL-retired, and the V3 storage
        // model is now the default for every deployment rather than something opted into. Making
        // the choice mandatory here would fail the startup of every deployment that simply took
        // the defaults. Deployments that want the requirement enforced set
        // CloudLoginTokens:SigningKeys:RequireExplicitStoreChoice themselves.
    }

    /// <summary>Registers CloudLogin using a concise options callback.</summary>
    public static void AddCloudLoginWeb(this IHostApplicationBuilder builder, Action<CloudLoginWebConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        CloudLoginWebConfiguration configuration = new();
        configure(configuration);
        builder.AddCloudLoginWeb(configuration);
    }

    private static void ConfigureSecurity(IServiceCollection services, CloudLoginWebConfiguration configuration)
    {
        services.AddDataProtection();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.Security.AuthenticationPermitLimit,
                        Window = configuration.Security.AuthenticationWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
    }

    private static void ConfigureCosmos(IHostApplicationBuilder builder, CloudLoginWebConfiguration loginConfig)
    {
        if (!loginConfig.Cosmos.IsValid())
            return;

        // Configure CloudLoginBaseRecord with Cosmos configuration for property naming
        CloudLoginBaseRecord.CosmosConfiguration = loginConfig.Cosmos;

        // Connection string or account endpoint with a credential - CosmosConfiguration owns that
        // choice, along with the custom serializer every client in this repository must carry.
        CosmosClient cosmosClient = loginConfig.Cosmos.CreateClient();

        // Shared so the core and provisioner use one client rather than
        // each building their own connection pool.
        builder.Services.TryAddSingleton(cosmosClient);
    }

    private static void ConfigureCloudWeb(IServiceCollection services, CloudLoginWebConfiguration loginConfig)
    {
        services.AddCloudWeb(config =>
        {
            config.PageDefaults.AppendBundle("css/site.css");
            config.PageDefaults.AppendBundle("css/preloaded.css");
            config.PageDefaults.AppendBundle("js/site.js");

            loginConfig.WebConfig(config);
            if (!string.IsNullOrWhiteSpace(loginConfig.Title))
                config.PageDefaults.SetTitle(loginConfig.Title);


            if (string.IsNullOrEmpty(config.PageDefaults.Title))
                config.PageDefaults.SetTitle("Login");

            config.PageDefaults.AppendBundle(new CloudBundle()
            {
                Source = "AngryMonkey.CloudLogin.WebAssembly.styles.css",
                MinOnRelease = false
            });

            // Bridges navigator.credentials to the passkey endpoints on the account page's
            // Security tab. Not pre-minified, so release builds must not look for a .min.js
            // that doesn't exist.
            config.PageDefaults.AppendBundle(new CloudBundle()
            {
                Source = "_content/AngryMonkey.CloudLogin.Components/cloudlogin-webauthn.js",
                MinOnRelease = false
            });
        });

        services.AddSingleton(loginConfig);
    }

    private static void ConfigureAuthentication(IServiceCollection services, CloudLoginWebConfiguration loginConfig)
    {
        AuthenticationBuilder auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                            .AddCookie(options => ConfigureCookieAuth(options, loginConfig))
                            .AddScheme<AuthenticationSchemeOptions, ServiceKeyAuthenticationHandler>(
                                ServiceKeyAuthenticationDefaults.AuthenticationScheme, null);

        new ProviderConfigurationService(loginConfig).ConfigureProviders(auth);
    }

    private static void ConfigureCookieAuth(CookieAuthenticationOptions options, CloudLoginWebConfiguration loginConfig)
    {
        options.Cookie.Name = loginConfig.CookieName;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = loginConfig.Security.RequireHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.Cookie.HttpOnly = true;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = loginConfig.Security.SessionIdleTimeout;
        options.SlidingExpiration = true;

        if (!string.IsNullOrWhiteSpace(loginConfig.CookieDomain))
            options.Cookie.Domain = loginConfig.CookieDomain;

        options.Events = new CookieAuthenticationEvents
        {
            OnSigningIn = async context =>
            {
                CloudLoginAuthenticationService authService = context.HttpContext.RequestServices.GetRequiredService<CloudLoginAuthenticationService>();
                context.Properties.Items.TryGetValue("cloudlogin:profile", out string? boundProfile);
                context.Properties.Items.TryGetValue("cloudlogin:profile_client", out string? profileClient);
                context.Principal = await authService.HandleSignIn(
                    context.Principal!, context.HttpContext, boundProfile, profileClient);
            }
        };
    }

}

public class CloudLoginWeb
{
    public static async Task InitApp(WebApplicationBuilder builder)
    {
        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
            app.UseWebAssemblyDebugging();
        else
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.MapStaticAssets();
        app.UseRouting();
        app.UseCloudLoginSecurity();
        app.UseAuthentication();
        app.UseAntiforgery();
        app.UseAuthorization();
        app.MapControllers();

        app.MapRazorComponents<AngryMonkey.CloudLogin.Main.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(AngryMonkey.CloudLogin.WebAssembly._Imports).Assembly);

        await app.RunAsync();
    }
}
