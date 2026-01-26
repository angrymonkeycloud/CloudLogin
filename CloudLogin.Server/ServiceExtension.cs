using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class MvcServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLoginServer(this IServiceCollection services, CloudLoginServerConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.AccessDeniedPath = "/auth/login";
                options.ReturnUrlParameter = "returnUrl";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Cookie.Name = config.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });

        // Add authorization services
        services.AddAuthorization();

        return services;
    }

    //public static IServiceCollection AddCloudLoginServer(this IServiceCollection services, Action<CloudLoginServerConfiguration> config)
    //{
    //    ArgumentNullException.ThrowIfNull(services);

    //    services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    //        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    //        {
    //            options.LoginPath = "/auth/login";
    //            options.LogoutPath = "/auth/logout";
    //            options.AccessDeniedPath = "/auth/login";
    //            options.ReturnUrlParameter = "returnUrl";
    //            options.ExpireTimeSpan = TimeSpan.FromDays(30);
    //            options.SlidingExpiration = true;
    //            options.Cookie.Name = config.CookieName;
    //            options.Cookie.HttpOnly = true;
    //            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    //            options.Cookie.SameSite = SameSiteMode.Lax;
    //        });

    //    // Add authorization services
    //    services.AddAuthorization();

    //    return services;
    //}
}
