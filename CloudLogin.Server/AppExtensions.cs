using AngryMonkey.CloudLogin;

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
					cloudLoginClient.RedirectUri = serverClient.RedirectUri;
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
