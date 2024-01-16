using AngryMonkey.CloudLogin;
using Microsoft.Extensions.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    public static async Task AddCloudLoginMVC(this IServiceCollection services, string loginServerUrl, string? baseUrl = null)
    {
        services.AddAuthentication("Cookies").AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.LoginPath = "/account/login";
            option.LogoutPath = "/account/logout";
        });

        CloudLoginClient cloudLoginClient = await CloudLoginClient.Build(loginServerUrl);

        if (!string.IsNullOrEmpty(baseUrl))
        {
            CloudLoginStandaloneClient cloudLoginStandaloneClient = CloudLoginStandaloneClient.Build(baseUrl);
        }
        services.AddSingleton(cloudLoginClient);
    }
}