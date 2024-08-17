using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;
using AngryMonkey.CloudWeb;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;

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

        services.AddAuthentication(opt => { opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme; });

        services.AddOptions();
        services.AddAuthenticationCore();

        services.AddScoped<CustomAuthenticationStateProvider>();
        services.AddScoped<UserController>();

        services.AddCloudWeb(config =>
        {
            loginConfig.WebConfig(config);

            if (string.IsNullOrEmpty(config.PageDefaults.Title))
                config.PageDefaults.SetTitle("Login");

            config.PageDefaults.AppendBundle(new CloudBundle() { Source = "AngryMonkey.CloudLogin.Components.styles.css", MinOnRelease = false });
        });

        loginConfig.FooterLinks.Add(new()
        {
            Url = "https://angrymonkeycloud.com/",
            Title = "Info"
        });

        CloudGeographyClient cloudGeography = new();

        services.AddSingleton(loginConfig);
        services.AddSingleton(cloudGeography);

        if (loginConfig.Cosmos != null)
            services.AddSingleton(sp => new CosmosMethods(loginConfig.Cosmos, cloudGeography));

        var service = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";

            if (!string.IsNullOrEmpty(loginConfig.BaseAddress) && loginConfig.BaseAddress != "localhost")
                option.Cookie.Domain = $".{loginConfig.BaseAddress}";

            option.Events = new CookieAuthenticationEvents()
            {
                OnSignedIn = async context =>
                {
                    //try
                    //{
                    HttpRequest? request = context.Request;
                    ClaimsPrincipal principal = context.Principal!;

                    // Do not continue on second sign in, in the future we should implemented in another way.
                    if (principal.FindFirst(ClaimTypes.Hash)?.Value?.Equals("CloudLogin") ?? false)
                        return;

                    CosmosMethods? cosmosMethods = context.HttpContext.RequestServices.GetService<CosmosMethods>();

                    if (cosmosMethods == null)
                        return;

                    DateTimeOffset currentDateTime = DateTimeOffset.UtcNow;

                    InputFormat formatValue = principal.HasClaim(claim => claim.Type == ClaimTypes.Email) ? InputFormat.EmailAddress : InputFormat.PhoneNumber;

                    string providerCode = principal.Identity!.AuthenticationType!;
                    string? providerUserID = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    string input = (formatValue == InputFormat.EmailAddress ? principal.FindFirst(ClaimTypes.Email)?.Value : principal.FindFirst(ClaimTypes.MobilePhone)?.Value)!;
                    User? user = formatValue == InputFormat.EmailAddress ? await cosmosMethods.GetUserByEmailAddress(input) : await cosmosMethods.GetUserByPhoneNumber(input);

                    LoginProvider provider = new() { Code = providerCode, Identifier = providerUserID };

                    if (providerCode.Equals("CloudLogin"))
                        switch (formatValue)
                        {
                            case InputFormat.EmailAddress:
                                provider = new() { Code = "CloudLogin", Identifier = providerUserID };
                                break;
                            case InputFormat.PhoneNumber:
                                provider = new() { Code = "WhatsApp", Identifier = providerUserID };
                                break;
                            default:
                                break;
                        }

                    bool existingUser = user != null;

                    if (existingUser)
                    {
                        user!.FirstName ??= principal.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
                        user!.LastName ??= principal.FindFirst(ClaimTypes.Surname)?.Value ?? "--";
                        user!.DisplayName ??= principal.FindFirst(ClaimTypes.Name)?.Value ?? $"{user!.FirstName} {user!.LastName}";

                        LoginInput? existingInput = user.Inputs.First(key => key.Input.Equals(input, StringComparison.OrdinalIgnoreCase));

                        if (!existingInput.Providers.Any(key => key.Code.Equals(provider.Code, StringComparison.OrdinalIgnoreCase)))
                            existingInput.Providers.Add(provider);

                        user.LastSignedIn = currentDateTime;

                        await cosmosMethods.Update(user);
                    }
                    else
                    {
                        string? countryCode = null;
                        string? callingCode = null;

                        if (formatValue == InputFormat.PhoneNumber)
                        {
                            CloudGeographyClient cloudGeography = context.HttpContext.RequestServices.GetService<CloudGeographyClient>()!;
                            PhoneNumber phoneNumber = cloudGeography.PhoneNumbers.Get(input);

                            input = phoneNumber.Number;
                            countryCode = phoneNumber.CountryCode;
                            callingCode = phoneNumber.CountryCallingCode;
                        }

                        string firstName = context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
                        string lastName = context.Principal?.FindFirst(ClaimTypes.Surname)?.Value ?? "--";

                        user = new()
                        {
                            ID = Guid.NewGuid(),
                            FirstName = firstName,
                            LastName = lastName,
                            DisplayName = principal.FindFirst(ClaimTypes.Name)?.Value ?? $"{firstName} {lastName}",
                            CreatedOn = currentDateTime,
                            LastSignedIn = currentDateTime,
                            Inputs =
                            [
                                new LoginInput()
                                {
                                    Input = input,
                                    Format = formatValue,
                                    IsPrimary = true,
                                    PhoneNumberCountryCode = countryCode,
                                    PhoneNumberCallingCode = callingCode,
                                    Providers = provider != null ? new() { provider } : new()
                                }
                            ]
                        };

                        await cosmosMethods.Create(user);
                    }
                    //}
                    //catch (Exception ex)
                    //{
                    //    EmailService emailService = context.HttpContext.RequestServices.GetService<EmailService>()!;

                    //    await emailService.SendEmail("Exception from OnSignedIn (Builder)", ex.ToString(), ["elietebchrani@live.com"]);
                    //}
                }
            };
        });

        foreach (ProviderConfiguration provider in loginConfig.Providers)
        {
            // Microsoft

            if (provider.GetType() == typeof(MicrosoftProviderConfiguration))
                service.AddMicrosoftAccount(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((MicrosoftProviderConfiguration)provider).ClientId;
                    Option.ClientSecret = ((MicrosoftProviderConfiguration)provider).ClientSecret;
                });

            // Google

            if (provider.GetType() == typeof(GoogleProviderConfiguration))
                service.AddGoogle(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((GoogleProviderConfiguration)provider).ClientId;
                    Option.ClientSecret = ((GoogleProviderConfiguration)provider).ClientSecret;
                });

            // Facebook

            if (provider.GetType() == typeof(FacebookProviderConfiguration))
                service.AddFacebook(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((FacebookProviderConfiguration)provider).ClientId;
                    Option.ClientSecret = ((FacebookProviderConfiguration)provider).ClientSecret;
                });

            // Twitter

            if (provider.GetType() == typeof(TwitterProviderConfiguration))
                service.AddTwitter(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ConsumerKey = ((TwitterProviderConfiguration)provider).ClientId;
                    Option.ConsumerSecret = ((TwitterProviderConfiguration)provider).ClientSecret;
                });
        }

        return services;
    }
}
public class CloudLoginWeb
{
    public static async Task InitApp(WebApplicationBuilder builder)
    {
        var app = builder.Build();

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