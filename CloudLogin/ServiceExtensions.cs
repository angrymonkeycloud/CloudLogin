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
	//public CloudLoginConfiguration Options { get; set; }
}

public static class MvcServiceCollectionExtensions
{

	public static CloudLoginService AddCloudLogin(this IServiceCollection services, HttpClient? httpServer = null)
	{
		CloudGeographyClient cloudGeography = new();

		CloudLoginClient cloudLoginClient = new() { HttpClient = httpServer };
		cloudLoginClient.FooterLinks.Add(new Link()
		{
			Url = "https://angrymonkeycloud.com/",
			Title = "Info"
		});

		services.AddSingleton(new CloudLoginService());
		services.AddSingleton(cloudGeography);
		services.AddSingleton(cloudLoginClient);

		services.AddAuthentication(opt =>
		{
			opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
		});

		services.AddOptions();
		services.AddAuthenticationCore();

		return null;
	}
}