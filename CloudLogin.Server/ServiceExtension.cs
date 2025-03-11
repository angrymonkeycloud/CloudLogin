using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static async Task AddCloudLogin(this IServiceCollection services, string loginServerUrl)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.LoginPath = "/account/login";
            option.LogoutPath = "/account/logout";
        });

        services.AddSingleton(await CloudLoginClient.Build(loginServerUrl, true));
    }
}