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
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.CookieName);

        if (config.SessionDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(config.SessionDuration));

        if (!string.IsNullOrWhiteSpace(config.LoginUrl) &&
            (!Uri.TryCreate(config.LoginUrl, UriKind.Absolute, out Uri? loginUri) ||
             (loginUri.Scheme != Uri.UriSchemeHttps && loginUri.Scheme != Uri.UriSchemeHttp)))
            throw new ArgumentException("LoginUrl must be an absolute HTTP or HTTPS URL.", nameof(config));

        services.AddControllers()
            .AddApplicationPart(typeof(global::AngryMonkey.CloudLogin.Server.Controllers.AuthController).Assembly);

        services.AddSingleton(config);
        services.AddHttpClient();
        services.AddDataProtection();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.AccessDeniedPath = "/auth/login";
                options.ReturnUrlParameter = "returnUrl";
                options.ExpireTimeSpan = config.SessionDuration;
                options.SlidingExpiration = true;
                options.Cookie.Name = config.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = config.RequireHttps
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
            });

        // Add authorization services
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Registers a consumer website with secure defaults. This is the shortest
    /// integration form when the login authority URL is known in code.
    /// </summary>
    public static IServiceCollection AddCloudLoginServer(
        this IServiceCollection services,
        string loginUrl,
        Action<CloudLoginServerConfiguration>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginUrl);

        CloudLoginServerConfiguration configuration = new() { LoginUrl = loginUrl };
        configure?.Invoke(configuration);
        return services.AddCloudLoginServer(configuration);
    }

}
