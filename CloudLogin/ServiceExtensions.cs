//using Microsoft.Extensions.DependencyInjection;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Components;
using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Cosmos;
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
        services.AddSingleton(new CloudLoginService() { Options = options });
        services.AddSingleton(new CloudGeographyClient());
        services.AddSingleton(new HttpClient());
        //services.AddSingleton<CloudLoginProcess>();

        var service = services.AddAuthentication("Cookies").AddCookie((option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.Events = new AspNetCore.Authentication.Cookies.CookieAuthenticationEvents()
            {
                OnSignedIn = async context =>
                {
                    DateTimeOffset currentDateTime = DateTimeOffset.UtcNow;

                    CloudUser? user ;
                    InputFormat FormatValue = InputFormat.EmailAddress;

                    string userID = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    string input = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;

                    if (string.IsNullOrEmpty(input))
                    {
                        input = context.Principal?.FindFirst(ClaimTypes.MobilePhone)?.Value;
                        user = await options.Cosmos.Methods.GetUserByPhoneNumber(input);
                        FormatValue = InputFormat.PhoneNumber;
                    }
                    else user = await options.Cosmos.Methods.GetUserByEmailAddress(input);

                    string providerCode = context.Principal?.Identity?.AuthenticationType;
                    string firstName = context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
                    string lastName = context.Principal?.FindFirst(ClaimTypes.Surname)?.Value ?? "--";


                    bool doesUserExist = user != null;

                    LoginProvider? provider = new()
                    {
                        Code = providerCode,
                        Identifier = userID
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
                    Option.ClientId = provider.ClientId;
                    Option.ClientSecret = provider.ClientSecret;
                });

            // Google

            if (provider.GetType() == typeof(CloudLoginConfiguration.GoogleAccount))
                service.AddGoogle(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = provider.ClientId;
                    Option.ClientSecret = provider.ClientSecret;
                });

            // Facebook

            if (provider.GetType() == typeof(CloudLoginConfiguration.FacebookAccount))
                service.AddFacebook(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ClientId = provider.ClientId;
                    Option.ClientSecret = provider.ClientSecret;
                });

            // Twitter

            if (provider.GetType() == typeof(CloudLoginConfiguration.TwitterAccount))
                service.AddTwitter(Option =>
                {
                    Option.SignInScheme = "Cookies";
                    Option.ConsumerKey = provider.ClientId;
                    Option.ConsumerSecret = provider.ClientSecret;
                });
        }

        if (options.MailMessage != null)
        {
            options.EmailMessageBody = options.MailMessage.Body ?? "{{code}}";
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
    public WhatsappAccount? Whatsapp { get; set; }
    public SmtpClient SmtpClient { get; set; }
    public MailMessage MailMessage { get; set; }
    public bool AllowLoginWithEmailCode { get; set; } = true;
    internal string EmailMessageBody { get; set; }

    public class Provider
    {
        public Provider(string code, string? label = null)
        {
            Code = code;
            Label = label ?? Code;
        }

        public string Code { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public bool HandlesEmailAddress { get; init; } = false;
        public bool HandlesPhoneNumber { get; init; } = false;
        public bool AlwaysShow { get; init; } = false;
    }

    public class MicrosoftAccount : Provider
    {
        public MicrosoftAccount(string? label = null) : base("Microsoft", label)
        {
            HandlesEmailAddress = true;
            //HandlesPhoneNumber = true;
        }
    }

    public class GoogleAccount : Provider
    {
        public GoogleAccount(string? label = null) : base("Google", label)
        {
            HandlesEmailAddress = true;
        }
    }
    public class FacebookAccount : Provider
    {
        public FacebookAccount(string? label = null) : base("Facebook", label)
        {
            HandlesEmailAddress = true;
            //HandlesPhoneNumber = true;
        }
    }
    public class TwitterAccount : Provider
    {
        public TwitterAccount(string? label = null) : base("Twitter", label)
        {
            HandlesEmailAddress = true;
            //HandlesPhoneNumber = true;
        }
    }
    public class EmailAccount : Provider
    {
        public EmailAccount(string? label = null) : base("Email", label)
        {
            HandlesEmailAddress = true;
            AlwaysShow = true;
        }
    }
    public class Whataspp : Provider
    {
        public Whataspp(string? label = null) : base("whatsapp", label)
        {
            //AlwaysShow = true;
            HandlesPhoneNumber = true;
        }
    }
    public class WhatsappAccount
    {
        public string RequestUri { get; set; }
        public string Authorization { get; set; }
        public string Template { get; set; }
        public string Language { get; set; }
    }

    public class CosmosDatabase
    {
        public string ConnectionString { get; set; }
        public string DatabaseId { get; set; }
        public string ContainerId { get; set; }

        private CosmosMethods? methods = null;
        internal CosmosMethods Methods => methods ??= new CosmosMethods(ConnectionString, DatabaseId, ContainerId);
    }

}