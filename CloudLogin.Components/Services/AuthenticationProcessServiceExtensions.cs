using AngryMonkey.CloudLogin.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin
{
    public static class AuthenticationProcessServiceExtensions
    {
        public static IServiceCollection AddAuthenticationProcessService(this IServiceCollection services)
        {
            services.AddScoped<AuthenticationProcessService>();
            return services;
        }
    }
}