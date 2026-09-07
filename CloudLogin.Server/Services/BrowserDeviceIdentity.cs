using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace AngryMonkey.CloudLogin.Server;

public static class BrowserDeviceIdentity
{
    public const string CookieName = "CloudLogin.Device";

    public static string GetOrCreate(HttpContext context)
    {
        IDataProtector protector = context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("CloudLogin.BrowserDevice.v1");
        string? identity = null;
        if (context.Request.Cookies.TryGetValue(CookieName, out string? cookie))
            try
            {
                string value = protector.Unprotect(cookie);
                if (Guid.TryParseExact(value, "N", out Guid parsed))
                    identity = parsed.ToString("N");
            }
            catch (CryptographicException) { }

        identity ??= Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(CookieName, protector.Protect(identity), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true
        });
        return identity;
    }
}