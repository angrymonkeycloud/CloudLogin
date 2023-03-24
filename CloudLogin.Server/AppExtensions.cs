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
				if (app.ApplicationServices.GetService(typeof(CloudLoginServerClient)) is CloudLoginServerClient cloudLoginClient && cloudLoginClient.HttpServer == null)
				{
					string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

					cloudLoginClient.HttpServer = new HttpClient() { BaseAddress = new Uri(baseUrl) };

					CloudLoginServerClient serverClient = await cloudLoginClient.InitFromServer();
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
