using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using AngryMonkey.CloudWeb;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

namespace Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLoginWeb(this IServiceCollection services, CloudLoginConfiguration loginConfig, IConfiguration builderConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRazorComponents()
            .AddInteractiveServerComponents()
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

        return services;
    }

    private static void ConfigureCloudWeb(IServiceCollection services, CloudLoginConfiguration loginConfig)
    {
        services.AddCloudWeb(config =>
        {
            config.PageDefaults.AppendBundle("css/site.css");
            config.PageDefaults.AppendBundle("css/preloaded.css");
            config.PageDefaults.AppendBundle("js/site.js");

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

        if (loginConfig.Cosmos != null)
            services.AddSingleton(sp => new CosmosMethods(loginConfig.Cosmos, new CloudGeographyClient()));
    }

    private static void ConfigureAuthentication(IServiceCollection services, CloudLoginConfiguration loginConfig)
    {
        var auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => ConfigureCookieAuth(options, loginConfig));

        var providerService = services.BuildServiceProvider()
            .GetRequiredService<ProviderConfigurationService>();

        providerService.ConfigureProviders(auth);
    }

    private static void ConfigureCookieAuth(CookieAuthenticationOptions options, CloudLoginConfiguration loginConfig)
    {
        options.Cookie.Name = "CloudLogin";

        if (!string.IsNullOrEmpty(loginConfig.BaseAddress) &&
            loginConfig.BaseAddress != "localhost")
            options.Cookie.Domain = $".{loginConfig.BaseAddress}";

        options.Events = new CookieAuthenticationEvents
        {
            OnSignedIn = async context =>
            {
                var authService = context.HttpContext.RequestServices
                    .GetRequiredService<CloudLoginAuthenticationService>();
                await authService.HandleSignIn(context.Principal!, context.HttpContext);
            }
        };
    }

    public static async Task ConfigCoconutSharp(this IHostApplicationBuilder builder, string[] args, CloudLoginConfiguration config)
    {
        builder.Configuration.AddAzureKeyVault(new Uri(args[0]), new DefaultAzureCredential());

        // Coconust Sharp
        //if (string.IsNullOrEmpty(config.Cosmos.ConnectionString))
        //    config.Cosmos.ConnectionString = builder.Configuration.GetValue<string>(CoconutSharpDefaults.Cosmos_ConnectionString);

        string tenantArg = args.First(key => key.StartsWith("tenantid:", StringComparison.OrdinalIgnoreCase));

        MicrosoftProviderConfiguration cspMicrosoft = await MicrosoftProviderConfiguration.FromAzureVault(new Uri(args[0]), tenantArg.Split(':')[1]);
        config.Providers.Insert(0, cspMicrosoft);
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
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAntiforgery();
        app.UseAuthorization();
        app.MapControllers();

        app.MapRazorComponents<AngryMonkey.CloudLogin.Main.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(AngryMonkey.CloudLogin._Imports).Assembly);

        await app.RunAsync();
    }
}