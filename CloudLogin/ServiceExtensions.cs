﻿//using Microsoft.Extensions.DependencyInjection;
using AngryMonkey.Cloud.Components;
using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using static Azure.Core.HttpHeader;
using User = AngryMonkey.Cloud.Login.DataContract.User;

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
		//services.AddSingleton<CloudLoginProcess>();

		var service = services.AddAuthentication("Cookies").AddCookie(option =>
		{
			option.Cookie.Name = "CloudLogin";
			option.Events = new AspNetCore.Authentication.Cookies.CookieAuthenticationEvents()
			{
				OnSignedIn = async context =>
				{
					string? emaillAddress = context.Principal?.FindFirst(ClaimTypes.Email)?.Value;

					if (string.IsNullOrEmpty(emaillAddress))
						return;


					User? user = await options.Cosmos.Methods.GetUserByEmailAddress(emaillAddress);

					bool doesUserExist = user != null;

					user ??= new User()
					{
						ID = Guid.NewGuid(),

						EmailAddresses = new()
							{
								new UserEmailAddress()
								{
									EmailAddress = emaillAddress,
									IsPrimary = true,
									ProviderId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
									Provider = context.Principal?.Identity?.AuthenticationType
								}
							}
					};

					user.LastSignedIn = DateTimeOffset.UtcNow;

					user.FirstName = context.Principal?.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
					user.LastName = context.Principal?.FindFirst(ClaimTypes.Surname)?.Value ?? "--";
					user.DisplayName = context.Principal?.FindFirst(ClaimTypes.Name)?.Value ?? $"{user.FirstName} {user.LastName}";

					if (doesUserExist)
						await options.Cosmos.Methods.Container.UpsertItemAsync(user);
					else
						await options.Cosmos.Methods.Container.CreateItemAsync(user);
				}
			};
		});

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

	public class Provider
	{
		public string Code { get; set; }
		public string ClientId { get; set; }
		public string ClientSecret { get; set; }
	}

	public class MicrosoftAccount : Provider
	{
		public MicrosoftAccount()
		{
			Code = "Microsoft";
		}
	}

	public class GoogleAccount : Provider
	{
		public GoogleAccount()
		{
			Code = "Google";
		}
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