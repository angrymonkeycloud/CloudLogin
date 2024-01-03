//using Microsoft.Extensions.DependencyInjection;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Microsoft.Extensions.DependencyInjection;


public static class MvcServiceCollectionExtensions
{
    public static void AddCloudLoginServer(this IServiceCollection services, CloudLoginConfiguration configuration)
    {
        configuration.FooterLinks.Add(new()
        {
            Url = "https://angrymonkeycloud.com/",
            Title = "Info"
        });

        CloudGeographyClient cloudGeography = new();

        services.AddSingleton(configuration);
        services.AddSingleton(cloudGeography);

        if (configuration.Cosmos != null)
            services.AddScoped(sp => new CosmosMethods(configuration.Cosmos, cloudGeography));

        var service = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";

            if (!string.IsNullOrEmpty(configuration.BaseAddress) && configuration.BaseAddress != "localhost")
                option.Cookie.Domain = $".{configuration.BaseAddress}";

            option.Events = new CookieAuthenticationEvents()
            {
                OnSignedIn = async context =>
                {
                    HttpRequest? request = context.Request;
                    ClaimsPrincipal principal = context.Principal!;

                    // Do not continue on second sign in, in the future we should implemented in another way.
                    if (principal.FindFirst(ClaimTypes.Hash)?.Value?.Equals("CloudLogin") ?? false)
                        return;

                    CosmosMethods cosmosMethods = context.HttpContext.RequestServices.GetService<CosmosMethods>()!;

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
                }
            };
        });

        foreach (ProviderConfiguration provider in configuration.Providers)
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
    }
}