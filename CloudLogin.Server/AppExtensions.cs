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

			app.Use((context, next) =>
			{
				if (app.ApplicationServices.GetService(typeof(CloudLoginClient)) is CloudLoginClient cloudLoginClient && cloudLoginClient.HttpClient == null)
				{
					string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

					cloudLoginClient.HttpClient = new HttpClient() { BaseAddress = new Uri(baseUrl) };

					cloudLoginClient.InitFromServer();
				}

				if (BaseController.Configuration == null)
					if (app.ApplicationServices.GetService(typeof(CloudLoginConfiguration)) is CloudLoginConfiguration cloudLoginConfiguration)
						BaseController.Configuration = cloudLoginConfiguration;

				return next.Invoke();
			});

			return app;
		}
	}
}
