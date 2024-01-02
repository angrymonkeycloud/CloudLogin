using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class MvcServiceCollectionExtensions
{
    public static async Task AddCloudLoginMVC(this IServiceCollection services, string loginServerUrl)
    {
        services.AddAuthentication("Cookies").AddCookie(option =>
        {
            option.Cookie.Name = "CloudLogin";
            option.LoginPath = "/account/login";
            option.LogoutPath = "/account/logout";
            //option.Events = new CookieAuthenticationEvents()
            //{
            //    OnSignedIn = async context =>
            //    {
            //    }
            //};
        });

        CloudLoginClient cloudLoginClient = await CloudLoginClient.Build(loginServerUrl);

        services.AddSingleton(cloudLoginClient);
        //services.AddSingleton(new CloudLoginController(cloudLoginClient));
    }
}