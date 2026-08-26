using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudLoginTokenAuthenticationExtensions
{
    /// <summary>
    /// The scheme applications should authorize against. It routes each request to
    /// the right handler: a bearer token when one is present, the session cookie
    /// otherwise. Application code just writes <c>[Authorize]</c>.
    /// </summary>
    public const string SchemeName = "CloudLogin";

    /// <summary>
    /// Configures this application to accept CloudLogin-issued access tokens, verified
    /// against the authority's published signing keys.
    /// <para>
    /// Verification needs only public key material, so this application holds nothing
    /// that would let it mint a token &mdash; compromising it cannot produce a forged
    /// identity.
    /// </para>
    /// <para>
    /// Also registers <see cref="ICloudLoginUserContext"/> and the outbound
    /// <see cref="CloudLoginTokenHandler"/>, so both directions of the identity flow
    /// are wired up by this single call.
    /// </para>
    /// </summary>
    public static IServiceCollection AddCloudLoginTokenAuthentication(
        this IServiceCollection services,
        Action<CloudLoginTokenClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        CloudLoginTokenClientOptions options = new();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.Authority))
            throw new InvalidOperationException("CloudLogin token authentication requires an Authority.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException(
                "CloudLogin token authentication requires an Audience. Without one, a token minted for any other service would be accepted here.");

        services.AddHttpContextAccessor();
        services.Configure(configure);

        services.AddHttpClient(CloudLoginTokenClientOptions.HttpClientName);

        // Delegated tokens are cached here, so a page that makes twenty downstream calls
        // performs one exchange rather than twenty.
        services.AddMemoryCache();

        services.AddScoped<ICloudLoginUserContext, CloudLoginUserContext>();
        services.AddScoped<ICloudLoginTokenProvider, CloudLoginTokenProvider>();

        // Built explicitly rather than by constructor injection: the handler's audience
        // is an optional constructor argument, which the container has no way to supply.
        services.AddTransient(provider => new CloudLoginTokenHandler(
            provider.GetRequiredService<ICloudLoginTokenProvider>(),
            provider.GetRequiredService<IOptions<CloudLoginTokenClientOptions>>()));

        services.AddAuthentication(SchemeName)
            .AddPolicyScheme(SchemeName, SchemeName, policy =>
            {
                policy.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString()
                        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                    (IsWebSocketHandshake(context) &&
                     !string.IsNullOrWhiteSpace(context.Request.Query["access_token"]))
                        ? JwtBearerDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;

                // Metadata is fetched over the wire, so it must not be fetched over
                // plaintext outside of loopback development.
                jwt.RequireHttpsMetadata =
                    !Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? authorityUri) ||
                    !authorityUri.IsLoopback;

                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority.TrimEnd('/'),
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // Pinning the algorithm stops an attacker from presenting a token
                    // signed with something weaker, or with "none".
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],

                    ClockSkew = TimeSpan.FromSeconds(30),
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    NameClaimType = CloudLoginClaims.Name,
                    RoleClaimType = "role"
                };

                jwt.Events = new JwtBearerEvents
                {
                    // A browser cannot set headers on a WebSocket handshake, so SignalR
                    // puts the token in the query string instead. Accepted only for an
                    // actual WebSocket upgrade or a SignalR negotiate: a token in a URL
                    // is a credential in every access log it passes through, and this
                    // keeps that to the one case that has no alternative.
                    OnMessageReceived = context =>
                    {
                        string? queryToken = context.Request.Query["access_token"];

                        if (!string.IsNullOrWhiteSpace(queryToken) && IsWebSocketHandshake(context.HttpContext))
                            context.Token = queryToken;

                        return Task.CompletedTask;
                    },

                    OnAuthenticationFailed = context =>
                    {
                        ILogger logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("CloudLogin.JwtBearer");

                        logger.LogDebug(context.Exception, "Bearer token rejected.");
                        return Task.CompletedTask;
                    }
                };
            });

        if (options.RequireAuthenticatedByDefault)
            // Deny by default: an endpoint becomes public only by saying so with
            // [AllowAnonymous], so forgetting to annotate a new controller fails
            // closed instead of exposing it.
            //
            // This covers *every* endpoint, including Razor pages, static assets and
            // the sign-in callback, so it suits API-only hosts. A host that also
            // serves anonymous pages should leave it off and mark its controllers
            // with [Authorize] instead.
            services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder(SchemeName)
                    .RequireAuthenticatedUser()
                    .Build());
        else
            services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Whether the request is a SignalR transport handshake &mdash; the WebSocket upgrade
    /// itself, or the negotiate that precedes it.
    /// </summary>
    private static bool IsWebSocketHandshake(HttpContext context) =>
        context.WebSockets.IsWebSocketRequest ||
        context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Configures token authentication from the canonical CloudLogin configuration section.</summary>
    public static IServiceCollection AddCloudLoginTokenAuthentication(this IServiceCollection services, IConfiguration configuration, Action<CloudLoginTokenClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddCloudLoginTokenAuthentication(options =>
        {
            configuration.GetSection("CloudLogin").Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Configures this relying party entirely from its host configuration.</summary>
    public static IHostApplicationBuilder AddCloudLoginTokenAuthentication(this IHostApplicationBuilder builder, Action<CloudLoginTokenClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCloudLoginTokenAuthentication(builder.Configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registers a typed <see cref="HttpClient"/> whose requests automatically carry
    /// the signed-in user's access token.
    /// </summary>
    /// <param name="audience">
    /// The audience of the service at <paramref name="baseAddress"/>. Give it whenever
    /// the target is a different service: an access token names exactly one audience,
    /// and this application's own token is rejected everywhere else. Leave it out only
    /// when the target validates this application's own audience, or when the service
    /// is already declared in <c>CloudLogin:DownstreamServices</c> and can be matched
    /// by base address.
    /// </param>
    public static IHttpClientBuilder AddCloudLoginAuthenticatedClient<TClient>(
        this IServiceCollection services,
        Uri baseAddress,
        string? audience = null)
        where TClient : class =>
        services
            .AddHttpClient<TClient>(client => client.BaseAddress = baseAddress)
            .AddCloudLoginToken(audience);

    /// <summary>
    /// Makes an already-registered <see cref="HttpClient"/> carry the signed-in user's
    /// access token, minted for <paramref name="audience"/>.
    /// </summary>
    public static IHttpClientBuilder AddCloudLoginToken(
        this IHttpClientBuilder builder,
        string? audience = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddHttpMessageHandler(provider => new CloudLoginTokenHandler(
            provider.GetRequiredService<ICloudLoginTokenProvider>(),
            provider.GetRequiredService<IOptions<CloudLoginTokenClientOptions>>(),
            audience));
    }
}
