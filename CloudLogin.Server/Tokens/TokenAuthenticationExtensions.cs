using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
        services.AddScoped<ICloudLoginUserContext, CloudLoginUserContext>();
        services.AddScoped<ICloudLoginTokenProvider, CloudLoginTokenProvider>();
        services.AddTransient<CloudLoginTokenHandler>();

        services.AddAuthentication(SchemeName)
            .AddPolicyScheme(SchemeName, SchemeName, policy =>
            {
                policy.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString()
                        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
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
    /// Registers a typed <see cref="HttpClient"/> whose requests automatically carry
    /// the signed-in user's access token.
    /// </summary>
    public static IHttpClientBuilder AddCloudLoginAuthenticatedClient<TClient>(
        this IServiceCollection services,
        Uri baseAddress)
        where TClient : class =>
        services
            .AddHttpClient<TClient>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<CloudLoginTokenHandler>();
}
