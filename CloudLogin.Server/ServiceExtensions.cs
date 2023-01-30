//using Microsoft.Extensions.DependencyInjection;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.Providers;
using CloudLoginDataContract;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Security.Claims;

namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginServerService
{
    IServiceCollection AddCloudLoginServer { get; }
}

public static class MvcServiceCollectionExtensions
{

    public static CloudLoginServerService? AddCloudLoginServer(this IServiceCollection services, CloudLoginConfiguration configuration)
    {
        configuration.FooterLinks.Add(new()
        {
            Url = "https://angrymonkeycloud.com/",
            Title = "Info"
        });

        services.AddSingleton(new CloudLoginServerService());
        services.AddSingleton(configuration);
        services.AddSingleton(new CloudLoginServerClient());

        CloudGeographyClient cloudGeography = new();

        var service = services.AddAuthentication("Cookies").AddCookie((option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.Events = new CookieAuthenticationEvents()
            {
                OnSignedIn = async context =>
                {
                    string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

                    CloudLoginServerClient cloudLogin = new()
                    {
                        HttpServer = new HttpClient() { BaseAddress = new Uri(baseUrl) }
                    };

                    DateTimeOffset currentDateTime = DateTimeOffset.UtcNow;

                    CloudUser? user;
                    InputFormat FormatValue = InputFormat.EmailAddress;

                    string? providerUserID = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    string? input = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                    string? hash = context.Principal?.FindFirst(ClaimTypes.Hash)?.Value;

                    if (hash == "Cloud Login")
                        return;
                    user = await cloudLogin.GetUserByPhoneNumber(input);

                    if (string.IsNullOrEmpty(input))
                    {
                        input = context.Principal?.FindFirst(ClaimTypes.MobilePhone)?.Value;
                        user = await cloudLogin.GetUserByPhoneNumber(input);
                        FormatValue = InputFormat.PhoneNumber;
                    }
                    else user = await cloudLogin.GetUserByEmailAddress(input);

                    string? providerCode = context.Principal?.Identity?.AuthenticationType;
                    string firstName = context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
                    string lastName = context.Principal?.FindFirst(ClaimTypes.Surname)?.Value ?? "--";


                    LoginProvider? provider = providerCode.Equals(".") ? null : new() { Code = providerCode, Identifier = providerUserID };

                    if (user != null)
                    {
                        if (provider != null)
                        {
                            try
                            {
                                LoginInput existingInput = user.Inputs.First(key => key.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
                                if (!existingInput.Providers.Select(key => key.Code.ToLower()).Contains(provider.Code.ToLower()))
                                    existingInput.Providers.Add(provider);
                            }
                            catch (Exception)
                            {
                                string countryCode = "", callingCode = "";
                                if (FormatValue == InputFormat.PhoneNumber)
                                {
                                    PhoneNumber phoneNumber = cloudGeography.PhoneNumbers.Get(input);

                                    input = phoneNumber.Number;
                                    countryCode = phoneNumber.CountryCode;
                                    callingCode = phoneNumber.CountryCallingCode;
                                }
                                user = new CloudUser()
                                {
                                    ID = Guid.NewGuid(),
                                    FirstName = firstName,
                                    LastName = lastName,
                                    DisplayName = context.Principal?.FindFirst(ClaimTypes.Name)?.Value ?? $"{firstName} {lastName}",
                                    CreatedOn = currentDateTime,
                                    Inputs = new()
                                    {
                                        new LoginInput()
                                        {
                                            Input = input,
                                            Format = FormatValue,
                                            IsPrimary = true,
                                            PhoneNumberCountryCode = countryCode,
                                            PhoneNumberCallingCode = callingCode
                                        }
                                    }
                                };

                            }


                        }
                    }
                    else
                    {
                        string? countryCode = null;
                        string? callingCode = null;

                        if (FormatValue == InputFormat.PhoneNumber)
                        {
                            PhoneNumber phoneNumber = cloudGeography.PhoneNumbers.Get(input);

                            input = phoneNumber.Number;
                            countryCode = phoneNumber.CountryCode;
                            callingCode = phoneNumber.CountryCallingCode;
                        }

                        user = new CloudUser()
                        {
                            ID = Guid.NewGuid(),
                            FirstName = firstName,
                            LastName = lastName,
                            DisplayName = context.Principal?.FindFirst(ClaimTypes.Name)?.Value ?? $"{firstName} {lastName}",
                            CreatedOn = currentDateTime,
                            Inputs = new()
                            {
                                new LoginInput()
                                {
                                    Input = input,
                                    Format = FormatValue,
                                    IsPrimary = true,
                                    PhoneNumberCountryCode = countryCode,
                                    PhoneNumberCallingCode = callingCode
                                }
                            }
                        };

                        if (provider != null)
                            user.Inputs.First().Providers.Add(provider);
                    }

                    user.LastSignedIn = currentDateTime;

                    if (user != null)
                        await cloudLogin.UpdateUser(user);
                    else
                        await cloudLogin.CreateUser(user);

                    context.HttpContext.Response.Cookies.Append("CloudUser",
                        JsonConvert.SerializeObject(user), new CookieOptions()
                        {
                            HttpOnly = true,
                            Secure = true,
                            Expires = DateTimeOffset.UtcNow.Add(configuration.LoginDuration)//CHANGE
                        });
                }
            };
        }));

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

        return null;
    }
}