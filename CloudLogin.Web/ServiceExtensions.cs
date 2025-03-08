using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;
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
        services.AddScoped<UserController>();

        services.AddCloudWeb(config =>
        {
            config.PageDefaults.AppendBundle("css/site.css");
            config.PageDefaults.AppendBundle("css/preloaded.css");
            config.PageDefaults.AppendBundle("js/site.js");

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

        AuthenticationBuilder service = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(option =>
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
                            DisplayName = (principal.FindFirst(ClaimTypes.Name) ?? principal.FindFirst("name"))?.Value ?? $"{firstName} {lastName}",
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
            {
                MicrosoftProviderConfiguration microsoftProvider = (MicrosoftProviderConfiguration)provider;

                MicrosoftProviderAudience audience = microsoftProvider.Audience;

                if (!string.IsNullOrEmpty(microsoftProvider.ClientSecret))
                    service.AddMicrosoftAccount(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((MicrosoftProviderConfiguration)provider).ClientId;
                    Option.ClientSecret = ((MicrosoftProviderConfiguration)provider).ClientSecret;

                    if (audience == MicrosoftProviderAudience.Personal)
                    {
                        Option.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
                        Option.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
                        Option.UserInformationEndpoint = "https://graph.microsoft.com/oidc/userinfo";
                    }

                    //// Set the RedirectUri
                    //Option.CallbackPath = new PathString("/signin-microsoft");
                    //Option.Events = new OAuthEvents
                    //{
                    //    OnRedirectToAuthorizationEndpoint = context =>
                    //    {
                    //        var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{Option.CallbackPath}";
                    //        context.Response.Redirect(context.RedirectUri + "&redirect_uri=" + Uri.EscapeDataString(redirectUri));
                    //        return Task.CompletedTask;
                    //    }
                    //};
                });
                else
                    service.AddOpenIdConnect("Microsoft", async options =>
                    {
                        options.SignInScheme = "Cookies";

                        string audiencePath = audience switch
                        {
                            MicrosoftProviderAudience.Personal => "consumers",
                            _ => ((MicrosoftProviderConfiguration)provider).TenantId!
                        };

                        options.ClientId = ((MicrosoftProviderConfiguration)provider).ClientId;
                        options.Authority = $"https://login.microsoftonline.com/{audiencePath}/v2.0/";
                        options.ResponseType = OpenIdConnectResponseType.Code;
                        options.SaveTokens = true;
                        options.GetClaimsFromUserInfoEndpoint = true;

                        X509Certificate2 certificate = await microsoftProvider.GetCertificate();

                        IConfidentialClientApplication? confidentialClient = null;

                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidIssuer = options.Authority,
                            ValidAudiences = [options.ClientId],
                            NameClaimType = ClaimTypes.Name
                        };

                        options.Scope.Add("User.Read");
                        options.Scope.Add(OpenIdConnectScope.Email);
                        options.Scope.Add(OpenIdConnectScope.Phone);
                        options.Scope.Add(OpenIdConnectScope.OfflineAccess);
                        options.Scope.Add(OpenIdConnectScope.Address);

                        options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "given_name");
                        options.ClaimActions.MapJsonKey(ClaimTypes.Surname, "family_name");

                        options.Events.OnAuthorizationCodeReceived = async context =>
                        {
                            string codeVerifier = context.TokenEndpointRequest.Parameters["code_verifier"];

                            string url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";

                            confidentialClient ??= ConfidentialClientApplicationBuilder.Create(options.ClientId)
                            .WithRedirectUri(url)
                            .WithCertificate(certificate)
                            .WithAuthority(new Uri(options.Authority))
                            .Build();

                            AuthenticationResult result = await confidentialClient.AcquireTokenByAuthorizationCode(["User.Read"], context.ProtocolMessage.Code)
                                                                                    .WithPkceCodeVerifier(codeVerifier)
                                                                                    .ExecuteAsync();

                            JwtSecurityTokenHandler handler = new();

                            JwtSecurityToken? accessToken = handler.ReadToken(result.AccessToken) as JwtSecurityToken;
                            JwtSecurityToken? idToken = handler.ReadToken(result.IdToken) as JwtSecurityToken;

                            // Extract claims from access token and id token
                            string? givenName = accessToken?.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
                            string? surname = accessToken?.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
                            string? email = accessToken?.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

                            email ??= accessToken?.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;

                            // Create ClaimsIdentity and add claims manually
                            ClaimsIdentity claimsIdentity = new("Microsoft");
                            claimsIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, accessToken?.Claims.FirstOrDefault(c => c.Type == "oid")?.Value));
                            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, email));

                            if (!string.IsNullOrEmpty(givenName))
                                claimsIdentity.AddClaim(new Claim(ClaimTypes.GivenName, givenName));

                            if (!string.IsNullOrEmpty(surname))
                                claimsIdentity.AddClaim(new Claim(ClaimTypes.Surname, surname));

                            foreach (Claim? claim in accessToken?.Claims.Concat(idToken?.Claims ?? Enumerable.Empty<Claim>()))
                                if (!claimsIdentity.HasClaim(claim.Type, claim.Value))
                                    claimsIdentity.AddClaim(claim);

                            ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

                            await context.HttpContext.SignInAsync("Cookies", claimsPrincipal);

                            context.HandleCodeRedemption(result.AccessToken, result.IdToken);
                        };
                    });
            }

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