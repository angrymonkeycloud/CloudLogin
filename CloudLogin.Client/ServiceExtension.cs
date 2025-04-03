using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static void AddCloudLogin(this IServiceCollection services, string loginServerUrl)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.LoginPath = "/account/login";
            option.LogoutPath = "/account/logout";
        });

        services.AddSingleton(sp => new CloudLoginClient()
        {
            HttpServer = new() { BaseAddress = new(loginServerUrl) }
        });
    }
}