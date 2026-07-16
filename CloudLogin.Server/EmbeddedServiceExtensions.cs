using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudWeb;
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
            OnSignedIn = async context =>
            {
                CloudLoginAuthenticationService authService = context.HttpContext.RequestServices.GetRequiredService<CloudLoginAuthenticationService>();
                await authService.HandleSignIn(context.Principal!, context.HttpContext);
            }
        };
    }
}
