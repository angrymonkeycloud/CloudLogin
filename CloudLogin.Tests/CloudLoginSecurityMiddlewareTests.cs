using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// The unsafe-method origin check in <c>UseCloudLoginSecurity</c>. The same middleware sends
/// <c>Referrer-Policy: no-referrer</c>, under which browsers post an ordinary same-origin HTML
/// form with <c>Origin: null</c> - so judging by Origin alone returned a bare 403 for every form
/// on every site using it (Angry Monkey Pay's "Continue to payment" among them). Fetch metadata
/// decides first; Origin is only the fallback for clients that do not send it.
/// </summary>
public class CloudLoginSecurityMiddlewareTests
{
    [Theory]
    [InlineData("same-origin", "null", 200)]
    [InlineData("same-origin", "https://app.test", 200)]
    [InlineData(null, "https://app.test", 200)]
    [InlineData(null, null, 200)]
    [InlineData("cross-site", "https://evil.test", 403)]
    [InlineData("cross-site", "null", 403)]
    [InlineData("same-site", "https://other.app.test", 403)]
    [InlineData(null, "https://evil.test", 403)]
    [InlineData(null, "null", 403)]
    public async Task Unsafe_requests_are_judged_by_fetch_metadata_before_origin(string? fetchSite, string? origin, int expected)
    {
        (RequestDelegate pipeline, IServiceProvider services) = Pipeline();
        DefaultHttpContext context = new() { RequestServices = services };
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.test");
        context.Request.Path = "/account";
        if (fetchSite is not null)
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        if (origin is not null)
            context.Request.Headers.Origin = origin;

        await pipeline(context);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Fact]
    public async Task Safe_requests_are_never_origin_checked()
    {
        (RequestDelegate pipeline, IServiceProvider services) = Pipeline();
        DefaultHttpContext context = new() { RequestServices = services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";

        await pipeline(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
    }

    private static (RequestDelegate Pipeline, IServiceProvider Services) Pipeline()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddRateLimiter();
        IServiceProvider provider = services.BuildServiceProvider();

        ApplicationBuilder app = new(provider);
        app.UseCloudLoginSecurity();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        return (app.Build(), provider);
    }
}
