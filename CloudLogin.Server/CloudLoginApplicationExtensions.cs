using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder;

public static class CloudLoginApplicationExtensions
{
    /// <summary>
    /// Enables CloudLogin rate limiting, security headers, origin checks, and
    /// no-store handling. Call after <c>UseRouting</c> and before endpoint mapping.
    /// </summary>
    public static IApplicationBuilder UseCloudLoginSecurity(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";

            PathString path = context.Request.Path;
            if (path.StartsWithSegments("/CloudLogin") ||
                path.StartsWithSegments("/Account") ||
                path.StartsWithSegments("/auth"))
            {
                headers.CacheControl = "no-store, no-cache, max-age=0";
                headers.Pragma = "no-cache";
            }

            if (!IsRemoteAuthenticationCallback(path) &&
                (HttpMethods.IsPost(context.Request.Method) ||
                 HttpMethods.IsPut(context.Request.Method) ||
                 HttpMethods.IsPatch(context.Request.Method) ||
                 HttpMethods.IsDelete(context.Request.Method)))
            {
                string? fetchSite = context.Request.Headers["Sec-Fetch-Site"].FirstOrDefault();
                if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                string? origin = context.Request.Headers.Origin.FirstOrDefault();
                string requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
                if (!string.IsNullOrWhiteSpace(origin) &&
                    !CloudLoginShared.IsSameOrigin(origin, requestOrigin))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await next(context);
        });

        return app;
    }

    /// <summary>
    /// Whether the path is a remote authentication callback such as <c>/signin-microsoft</c>.
    /// </summary>
    /// <remarks>
    /// An identity provider posts the authorization code back from its own origin, so these are
    /// cross-site POSTs by design - a certificate-backed Microsoft app registration has no client
    /// secret, which puts sign-in on the OpenID Connect handler and its <c>form_post</c> response
    /// mode. The cross-site checks below would reject that with a bare 403 before the
    /// authentication middleware ever runs. The callbacks are not left unprotected: the remote
    /// handler validates the OAuth state parameter against a correlation cookie it set itself.
    /// </remarks>
    private static bool IsRemoteAuthenticationCallback(PathString path)
    {
        if (!path.HasValue)
            return false;

        ReadOnlySpan<char> remaining = path.Value.AsSpan(1);
        int separator = remaining.IndexOf('/');
        ReadOnlySpan<char> segment = separator < 0 ? remaining : remaining[..separator];

        return segment.StartsWith("signin-", StringComparison.OrdinalIgnoreCase)
            || segment.StartsWith("signout-callback-", StringComparison.OrdinalIgnoreCase);
    }
}
