//using Microsoft.Extensions.DependencyInjection;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Components;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Twilio;
using Twilio.Exceptions;
using static Azure.Core.HttpHeader;
using CloudUser = AngryMonkey.Cloud.Login.DataContract.CloudUser;

namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginService
{
    IServiceCollection AddCloudLogin { get; }
    public CloudLoginConfiguration Options { get; set; }
}

public static class MvcServiceCollectionExtensions
{

    public static CloudLoginService AddCloudLogin(this IServiceCollection services, CloudLoginConfiguration options)
    {
        CloudGeographyClient cloudGeography = new();

        services.AddSingleton(new CloudLoginService() { Options = options });
        services.AddSingleton(cloudGeography);
        services.AddSingleton(new HttpClient());
		services.AddScoped<AngryMonkey.Cloud.Login.Controllers.CustomAuthenticationStateProvider>();

		services.AddAuthentication(opt =>
		{
			opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
		});

		services.AddOptions();
		services.AddAuthenticationCore();

		var service = services.AddAuthentication("Cookies").AddCookie((option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.Events = new AspNetCore.Authentication.Cookies.CookieAuthenticationEvents()
            {
                OnSignedIn = async context =>
                {
                    DateTimeOffset currentDateTime = DateTimeOffset.UtcNow;

                    CloudUser? user;
                    InputFormat FormatValue = InputFormat.EmailAddress;

                    string providerUserID = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    string input = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                    string hash = context.Principal?.FindFirst(ClaimTypes.Hash)?.Value;

                    if (hash == "Cloud Login")
                        return;

                    if (string.IsNullOrEmpty(input))
                    {
                        input = context.Principal?.FindFirst(ClaimTypes.MobilePhone)?.Value;
                        user = await options.Cosmos.Methods.GetUserByPhoneNumber(input);
                        FormatValue = InputFormat.PhoneNumber;
                    }
                    else user = await options.Cosmos.Methods.GetUserByEmailAddress(input);

                    string? providerCode = context.Principal?.Identity?.AuthenticationType;
                    string firstName = context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
                    string lastName = context.Principal?.FindFirst(ClaimTypes.Surname)?.Value ?? "--";

                    bool doesUserExist = user != null;

                    LoginProvider? provider = providerCode.Equals(".") ? null : new()
                    {
                        Code = providerCode,
                        Identifier = providerUserID
                    };

                    if (doesUserExist)
                    {
                        if (provider != null)
                        {
                            LoginInput existingInput = user.Inputs.First(key => key.Input.Equals(input, StringComparison.OrdinalIgnoreCase));

                            if (!existingInput.Providers.Select(key => key.Code.ToLower()).Contains(provider.Code.ToLower()))
                                existingInput.Providers.Add(provider);
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

                    if (doesUserExist)
                        await options.Cosmos.Methods.Container.UpsertItemAsync(user);
                    else
                        await options.Cosmos.Methods.Container.CreateItemAsync(user);

                    context.HttpContext.Response.Cookies.Append("CloudUser",
                        JsonConvert.SerializeObject(user), new CookieOptions()
                        {
                            HttpOnly = true,
                            Secure = true
                        });

                    //ClaimsIdentity identity = context.Principal.Identity as ClaimsIdentity;
                    //identity.RemoveClaim(context.Principal?.FindFirst(ClaimTypes.NameIdentifier));

                    //context.Principal.Claims.Append(new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()));
                }
            };
        }));

        foreach (CloudLoginConfiguration.Provider provider in options.Providers)
        {
            // Microsoft

            if (provider.GetType() == typeof(CloudLoginConfiguration.MicrosoftAccount))
                service.AddMicrosoftAccount(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((CloudLoginConfiguration.MicrosoftAccount)provider).ClientId;
                    Option.ClientSecret = ((CloudLoginConfiguration.MicrosoftAccount)provider).ClientSecret;
                });

            // Google

            if (provider.GetType() == typeof(CloudLoginConfiguration.GoogleAccount))
                service.AddGoogle(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((CloudLoginConfiguration.GoogleAccount)provider).ClientId;
                    Option.ClientSecret = ((CloudLoginConfiguration.GoogleAccount)provider).ClientSecret;
                });

            // Facebook

            if (provider.GetType() == typeof(CloudLoginConfiguration.FacebookAccount))
                service.AddFacebook(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = ((CloudLoginConfiguration.FacebookAccount)provider).ClientId;
                    Option.ClientSecret = ((CloudLoginConfiguration.FacebookAccount)provider).ClientSecret;
                });

            // Twitter

            if (provider.GetType() == typeof(CloudLoginConfiguration.TwitterAccount))
                service.AddTwitter(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ConsumerKey = ((CloudLoginConfiguration.TwitterAccount)provider).ClientId;
                    Option.ConsumerSecret = ((CloudLoginConfiguration.TwitterAccount)provider).ClientSecret;
                });
        }

        return null;
    }
}

//public class CloudLoginProcess
//{
//	public string EmailAddress { get; set; }
//	public string RedirectUrl { get; set; }
//	public string Identity { get; set; }
//}

public class CloudLoginConfiguration
{
    public List<Provider> Providers { get; set; } = new();
    public CosmosDatabase? Cosmos { get; set; }
    public List<Links>? CloudLoginFooterLink { get; set; }
    public string? RedirectUrl { get; set; }
    internal string EmailMessageBody { get; set; }
    public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; } = null;

    public class Provider
    {
        public Provider(string code, string? label = null)
        {
            Code = code;
            Label = label ?? Code;
        }

        public string Code { get; init; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        internal bool HandlesEmailAddress { get; init; } = false;
        internal bool HandlesPhoneNumber { get; set; } = false;
        internal bool IsCodeVerification { get; init; } = false;

        internal string CssClass
        {
            get
            {
                List<string> classes = new()
                {
                    $"_{Code.ToLower()}"
                };

                return string.Join(" ", classes);
            }
        }
    }

    public class MicrosoftAccount : Provider
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public MicrosoftAccount(string? label = null) : base("Microsoft", label)
        {
            HandlesEmailAddress = true;
        }
    }

    public class GoogleAccount : Provider
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public GoogleAccount(string? label = null) : base("Google", label)
        {
            HandlesEmailAddress = true;
        }
    }

    public class FacebookAccount : Provider
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public FacebookAccount(string? label = null) : base("Facebook", label)
        {
            HandlesEmailAddress = true;
        }
    }

    public class TwitterAccount : Provider
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public TwitterAccount(string? label = null) : base("Twitter", label)
        {
            HandlesEmailAddress = true;
        }
    }

    public class Whataspp : Provider
    {
        public string RequestUri { get; set; }
        public string Authorization { get; set; }
        public string Template { get; set; }
        public string Language { get; set; }

        public Whataspp(string? label = null) : base("whatsapp", label)
        {
            HandlesPhoneNumber = true;
            IsCodeVerification = true;
        }
    }

    public class Links
    {
        public string Title { get; set; }
        public string Url { get; set; }

    }

        public class CosmosDatabase
    {
        public string ConnectionString { get; set; }
        public string DatabaseId { get; set; }
        public string ContainerId { get; set; }

        private CosmosMethods? methods = null;
        public CosmosMethods Methods => methods ??= new CosmosMethods(ConnectionString, DatabaseId, ContainerId);
    }

    public class SendCodeValue
    {
        public SendCodeValue(string code, string address)
        {
            Code = code;
            Address = address;
        }

        public string Code { get; set; }
        public string Address { get; set; }
    }
}