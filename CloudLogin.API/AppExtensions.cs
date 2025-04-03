namespace Microsoft.AspNetCore.Builder;

public static class MvcBuilderExtensions
{
    public static IApplicationBuilder UseCloudLoginHandler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(nameof(app));

        app.Use(async (context, next) =>
        {
            string baseUrl = $"http{(context.Request.IsHttps ? "s" : string.Empty)}://{context.Request.Host.Value}";

            string? isLoggedIn = context.Request.Cookies["AutomaticSignIn"];
            string? hasData = context.Request.Cookies["CloudLogin"];
            string? isLogginIn = context.Request.Cookies["LoggingIn"];

            if (isLogginIn == null && hasData == null && (isLoggedIn != null || hasData != null))
                context.Response.Redirect($"{baseUrl}/Account/Login");

            await next.Invoke();
        });

        app.UseAuthorization();

        return app;
    }

}
