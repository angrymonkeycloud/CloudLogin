using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Storage;
using AngryMonkey.CloudLogin.Server.Versioning.V1;
using AngryMonkey.CloudBlazor.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class MvcServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLoginEmbedded(this IServiceCollection services, CloudLoginWebConfiguration loginConfig, IConfiguration builderConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loginConfig);

        bool isDevelopment = string.Equals(
            builderConfiguration["ASPNETCORE_ENVIRONMENT"],
            "Development",
            StringComparison.OrdinalIgnoreCase);
        CloudLoginConfigurationValidator.Validate(loginConfig, isDevelopment);

        services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents();

        //CloudWebConfig? webConfig = builderConfiguration.Get<CloudWebConfig>();

        //services.AddAuthentication(options =>
        //{
        //    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        //    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        //});

        services.AddOptions();
        services.AddAuthenticationCore();
        services.AddScoped<CustomAuthenticationStateProvider>();
        services.AddScoped<ProviderConfigurationService>();
        services.AddDataProtection();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(CloudLoginSecurityDefaults.AuthenticationRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = loginConfig.Security.AuthenticationPermitLimit,
                        Window = loginConfig.Security.AuthenticationWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        ConfigureCloudWeb(services, loginConfig);
        ConfigureAuthentication(services, loginConfig);

        services.AddCloudLoginWeb(loginConfig);

        // Modern storage core plus API façade validation, mirroring the standalone host.
        services.AddCloudLoginCore(loginConfig);
        services.EnsureVersion1Implemented(loginConfig.ApiVersion);

        // CloudLogin creates its own database and containers here too: an embedded host gets the
        // same schema ownership as the standalone site, with or without an AppHost.
        services.AddCloudLoginStorageProvisioning();

        // The selected façade also answers at unversioned routes.
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
            options.Conventions.Add(new AngryMonkey.CloudLogin.Server.Versioning.SelectedApiVersionRouteConvention(loginConfig.ApiVersion)));

        return services;
    }

    private static void ConfigureCloudWeb(IServiceCollection services, CloudLoginWebConfiguration loginConfig)
    {
        services.AddCloudWeb(config =>
        {
            //config.PageDefaults.AppendBundle("css/site.css");
            //config.PageDefaults.AppendBundle("css/preloaded.css");
            //config.PageDefaults.AppendBundle("js/site.js");

            loginConfig.WebConfig(config);

            if (string.IsNullOrEmpty(config.PageDefaults.Title))
                config.PageDefaults.SetTitle("Login");

            config.PageDefaults.AppendBundle(new CloudBundle()
            {
                Source = "AngryMonkey.CloudLogin.Components.styles.css",
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
                            .AddCookie(options => ConfigureCookieAuth(options, loginConfig));

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
