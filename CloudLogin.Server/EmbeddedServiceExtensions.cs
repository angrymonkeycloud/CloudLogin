using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class MvcServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLoginEmbedded(this IServiceCollection services, CloudLoginWebConfiguration loginConfig, IConfiguration builderConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);

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

        ProviderConfigurationService providerService = services.BuildServiceProvider().GetRequiredService<ProviderConfigurationService>();

        providerService.ConfigureProviders(auth);
    }

    private static void ConfigureCookieAuth(CookieAuthenticationOptions options, CloudLoginWebConfiguration loginConfig)
    {
        options.Cookie.Name = "CloudLogin";
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        if (!string.IsNullOrEmpty(loginConfig.BaseAddress) && loginConfig.BaseAddress != "localhost")
            options.Cookie.Domain = $".{loginConfig.BaseAddress}";

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