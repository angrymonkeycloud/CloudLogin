using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin;

public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Registers CloudLogin for Blazor WebAssembly / web apps.
    /// In Program.cs: builder.Services.AddWebCloudLogin()
    /// </summary>
    public static IServiceCollection AddWebCloudLogin(this IServiceCollection services)
    {
        services.AddScoped<ICloudLoginService, CloudLoginWebService>();
        return services;
    }
}
