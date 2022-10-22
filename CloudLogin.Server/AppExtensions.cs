using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using AngryMonkey.Cloud.Login.Controllers;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.Extensions.Options;
using AngryMonkey.Cloud.Login;

namespace Microsoft.AspNetCore.Builder
{
	public static class CloudLoginBuilderExtensions
	{
		public static IApplicationBuilder UseCloudLogin(this IApplicationBuilder app)
		{
			if (app == null)
				throw new ArgumentNullException(nameof(app));

			app.Use(async (context, next) =>
			{
				if (app.ApplicationServices.GetService(typeof(CloudLoginClient)) is CloudLoginClient cloudLoginClient && cloudLoginClient.HttpServer == null)
				{
					string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

					cloudLoginClient.HttpServer = new HttpClient() { BaseAddress = new Uri(baseUrl) };

					CloudLoginClient serverClient = await cloudLoginClient.InitFromServer();
					cloudLoginClient.Providers = serverClient.Providers;
					cloudLoginClient.FooterLinks = serverClient.FooterLinks;
					cloudLoginClient.RedirectUrl = serverClient.RedirectUrl;
					cloudLoginClient.UsingDatabase = serverClient.UsingDatabase;
				}

				if (BaseController.Configuration == null)
					if (app.ApplicationServices.GetService(typeof(CloudLoginConfiguration)) is CloudLoginConfiguration cloudLoginConfiguration)
						BaseController.Configuration = cloudLoginConfiguration;

				await next.Invoke();
			});

			return app;
		}
	}
}
