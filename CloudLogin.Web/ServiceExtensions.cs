using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using AngryMonkey.CloudWeb;
using Azure.Identity;
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

namespace Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static void AddCloudLoginWeb(this IHostApplicationBuilder builder, CloudLoginConfiguration loginConfig)
    {
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();

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

        builder.Services.AddCloudLoginServer(loginConfig);
    }

    private static void ConfigureCosmos(IHostApplicationBuilder builder, CloudLoginConfiguration loginConfig)
    {
        if (!loginConfig.Cosmos.IsValid())
            return;

        // Create CosmosClient with custom serialization to exclude null values and serialize enums as strings
        CosmosClient cosmosClient = new(loginConfig.Cosmos.ConnectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions 
            { 
                IgnoreNullValues = true
            }
        });

        //if (!string.IsNullOrEmpty(loginConfig.Cosmos.AspireName))
        //    builder.Services.AddAzureCosmosContainer("cosmos",
        //        configureSettings: settings =>
        //        {
        //            settings.DatabaseName = "cosmos-database";
        //            settings.ContainerName = "cosmos-container";
        //        },
        //        configureClientOptions: options =>
        //        {
        //            options.Serializer = new SystemTextJsonCosmosSerializer(cosmosSerialization);
        //        });
        //else
        //builder.AddAzureCosmosClient(loginConfig.Cosmos.ConnectionString, configureClientOptions: options =>
        //{
        //    options.Serializer = new SystemTextJsonCosmosSerializer(cosmosSerialization);
        //});
        //builder.AddAzureCosmosContainer(loginConfig.Cosmos.ConnectionString,
        //    configureSettings: settings =>
        //    {
        //        settings.DatabaseName = loginConfig.Cosmos.DatabaseId;
        //        settings.ContainerName = loginConfig.Cosmos.ContainerId;
        //    },
        //    configureClientOptions: options =>
        //    {
        //        options.Serializer = new SystemTextJsonCosmosSerializer(cosmosSerialization);
        //    });

        // Get container reference
        var container = cosmosClient.GetContainer(loginConfig.Cosmos.DatabaseId, loginConfig.Cosmos.ContainerId);

        // Register as singleton
        builder.Services.AddSingleton(container);
        builder.Services.AddScoped<CosmosMethods>();
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
                Source = "AngryMonkey.CloudLogin.WASM.styles.css",
                MinOnRelease = false
            });
        });

        services.AddSingleton(loginConfig);
    }

    private static void ConfigureAuthentication(IServiceCollection services, CloudLoginConfiguration loginConfig)
    {
        AuthenticationBuilder auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                            .AddCookie(options => ConfigureCookieAuth(options, loginConfig));

        ProviderConfigurationService providerService = services.BuildServiceProvider().GetRequiredService<ProviderConfigurationService>();

        providerService.ConfigureProviders(auth);
    }

    private static void ConfigureCookieAuth(CookieAuthenticationOptions options, CloudLoginConfiguration loginConfig)
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

    public static async Task ConfigCoconutSharp(this IHostApplicationBuilder builder, string[] args, CloudLoginConfiguration config)
    {
        builder.Configuration.AddAzureKeyVault(new Uri(args[0]), new DefaultAzureCredential());

        // Coconust Sharp
        //if (string.IsNullOrEmpty(config.Cosmos.ConnectionString))
        //    config.Cosmos.ConnectionString = builder.Configuration.GetValue<string>(CoconutSharpDefaults.Cosmos_ConnectionString);

        string tenantArg = args.First(key => key.StartsWith("tenantid:", StringComparison.OrdinalIgnoreCase));

        LoginProviders.MicrosoftProviderConfiguration cspMicrosoft = await LoginProviders.MicrosoftProviderConfiguration.FromAzureVault(new Uri(args[0]), tenantArg.Split(':')[1]);
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
        app.MapStaticAssets();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAntiforgery();
        app.UseAuthorization();
        app.MapControllers();

        app.MapRazorComponents<AngryMonkey.CloudLogin.Main.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(AngryMonkey.CloudLogin.WASM._Imports).Assembly);

        await app.RunAsync();
    }
}