using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Components;

namespace Microsoft.AspNetCore.Builder
{
    public static class MvcBuilderExtensions
    {
		public static IApplicationBuilder UseCloudLoginHandler(this IApplicationBuilder app)
		{
			if (app == null)
				throw new ArgumentNullException(nameof(app));

			app.Use(async (context, next) =>
			{
				if (app.ApplicationServices.GetService(typeof(CloudLoginClient)) is CloudLoginClient cloudLoginClient)
				{
					string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

                    if (app.ApplicationServices.GetService(typeof(NavigationManager)) is NavigationManager nav)
                        if (await cloudLoginClient.AutomaticLogin())
                            nav.NavigateTo($"{baseUrl}Account/login");
                }


				await next.Invoke();
			});

			return app;
		}
	}
}
