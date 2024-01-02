//using AngryMonkey.CloudLogin;

//namespace Microsoft.AspNetCore.Builder
//{
//    public static class CloudLoginBuilderExtensions
//	{
//		public static IApplicationBuilder UseCloudLogin(this IApplicationBuilder app)
//		{
//			if (app == null)
//				throw new ArgumentNullException(nameof(app));

//			app.Use(async (context, next) =>
//			{
//				if (app.ApplicationServices.GetService(typeof(CloudLoginClient)) is CloudLoginClient cloudLoginClient && cloudLoginClient.HttpServer == null)
//				{
//					string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

//					cloudLoginClient = new(baseUrl);

//					CloudLoginClient serverClient = await cloudLoginClient.Init();
//					cloudLoginClient.Providers = serverClient.Providers;
//					cloudLoginClient.FooterLinks = serverClient.FooterLinks;
//					cloudLoginClient.RedirectUri = serverClient.RedirectUri;
//					cloudLoginClient.UsingDatabase = serverClient.UsingDatabase;
//				}

//				await next.Invoke();
//			});

//			return app;
//		}
//	}
//}
