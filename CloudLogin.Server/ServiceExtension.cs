using AngryMonkey.CloudLogin;
using Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static async Task AddCloudLogin(this IServiceCollection services, string loginServerUrl)
    {
        services.AddAuthentication("Cookies").AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.LoginPath = "/account/login";
            option.LogoutPath = "/account/logout";
        });

        services.AddSingleton(await CloudLoginClient.Build(loginServerUrl, true));
    }
}