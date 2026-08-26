using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Supplies a valid access token for the current user, refreshing it when it is
/// close to expiry, and delegating it to another audience when the call is bound for
/// a different service.
/// <para>
/// Tokens live inside the relying party's authentication cookie, which is encrypted
/// with Data Protection and marked HttpOnly. They are therefore never reachable from
/// JavaScript &mdash; the reason this design keeps cookies for browsers instead of
/// handing tokens to the front end.
/// </para>
/// </summary>
public interface ICloudLoginTokenProvider
{
    /// <summary>
    /// A currently valid access token for the signed-in user, minted for this
    /// application's own audience. <see langword="null"/> when the request is
    /// anonymous or the session can no longer be refreshed.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A currently valid access token for the signed-in user, valid at
    /// <paramref name="audience"/>.
    /// <para>
    /// An access token is deliberately only accepted by the one service it names, so
    /// calling a different service requires a different token. When
    /// <paramref name="audience"/> is another service, this performs a delegated
    /// exchange at the authority: the user stays the subject, and this application is
    /// recorded as the actor. Passing this application's own audience, or
    /// <see langword="null"/>, returns the session token unchanged.
    /// </para>
    /// </summary>
    /// <param name="audience">Audience of the service about to be called.</param>
    /// <param name="forceRefresh">
    /// Bypasses the delegation cache. Use after a downstream service rejected a token,
    /// not as a matter of course &mdash; every forced refresh is a round trip to the
    /// authority.
    /// </param>
    Task<string?> GetAccessTokenAsync(
        string? audience,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CloudLoginTokenProvider(
    IHttpContextAccessor accessor,
    IHttpClientFactory httpClientFactory,
    IOptions<CloudLoginTokenClientOptions> options,
    IMemoryCache cache,
    ILogger<CloudLoginTokenProvider> logger) : ICloudLoginTokenProvider
{
    internal const string AccessTokenName = "cloudlogin.access_token";
    internal const string RefreshTokenName = "cloudlogin.refresh_token";
    internal const string ExpiresAtName = "cloudlogin.expires_at";

    private readonly CloudLoginTokenClientOptions _options = options.Value;

    /// <summary>
    /// Refresh this long before actual expiry, so a token never expires mid-flight
    /// on a downstream call that has already been dispatched.
    /// </summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(60);

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        GetAccessTokenAsync(audience: null, forceRefresh: false, cancellationToken);

    public async Task<string?> GetAccessTokenAsync(
        string? audience,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        string? sessionToken = await GetSessionTokenAsync(cancellationToken);

        if (sessionToken is null)
            return null;

        if (string.IsNullOrWhiteSpace(audience) ||
            string.Equals(audience, _options.Audience, StringComparison.Ordinal))
            return sessionToken;

        return await GetDelegatedTokenAsync(sessionToken, audience, forceRefresh, cancellationToken);
    }

    /// <summary>
    /// The token this application was issued at sign-in, refreshed when it is close to
    /// expiry. This is the credential every delegated token is built on, so it must be
    /// current before anything is derived from it.
    /// </summary>
    private async Task<string?> GetSessionTokenAsync(CancellationToken cancellationToken)
    {
        HttpContext? context = accessor.HttpContext;

        if (context is null)
            return null;

        AuthenticateResult result = await context.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (!result.Succeeded || result.Properties is null)
            return null;

        string? accessToken = result.Properties.GetTokenValue(AccessTokenName);
        string? refreshToken = result.Properties.GetTokenValue(RefreshTokenName);
        string? expiresAtRaw = result.Properties.GetTokenValue(ExpiresAtName);

        bool expiringSoon =
            !DateTimeOffset.TryParse(expiresAtRaw, out DateTimeOffset expiresAt) ||
            DateTimeOffset.UtcNow.Add(RefreshWindow) >= expiresAt;

        if (!expiringSoon && !string.IsNullOrWhiteSpace(accessToken))
            return accessToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
            return accessToken;

        CloudLoginTokenResponse? refreshed = await RefreshAsync(refreshToken, cancellationToken);

        if (refreshed is null)
        {
            // The refresh chain is gone: either it expired, or reuse detection burned
            // it. Either way this session cannot continue, so drop the cookie rather
            // than letting the user linger in a half-authenticated state.
            logger.LogInformation("CloudLogin refresh failed; signing the session out.");
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }

        result.Properties.StoreTokens(
        [
            new AuthenticationToken { Name = AccessTokenName, Value = refreshed.AccessToken },
            new AuthenticationToken { Name = RefreshTokenName, Value = refreshed.RefreshToken ?? refreshToken },
            new AuthenticationToken
            {
                Name = ExpiresAtName,
                Value = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn).ToString("o")
            }
        ]);

        // Re-issue the cookie so the rotated refresh token is what the browser carries
        // next time. Skipping this would replay a consumed token and trip reuse detection.
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            result.Principal!,
            result.Properties);

        return refreshed.AccessToken;
    }

    /// <summary>
    /// Trades this application's own token for one valid at <paramref name="audience"/>.
    /// <para>
    /// Cached against the exact subject token it was derived from, so a delegation
    /// cannot outlive the session token behind it: when that token rotates, everything
    /// derived from it stops being served from cache on the very next call.
    /// </para>
    /// </summary>
    private async Task<string?> GetDelegatedTokenAsync(
        string subjectToken,
        string audience,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            logger.LogWarning(
                "A call bound for audience {Audience} needs a delegated token, but this application has no CloudLogin service-client credentials configured. The call will carry no identity.",
                audience);
            return null;
        }

        string cacheKey = DelegationCacheKey(subjectToken, audience);

        if (forceRefresh)
            cache.Remove(cacheKey);
        else if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        CloudLoginTokenResponse? delegated = await ExchangeAsync(subjectToken, audience, cancellationToken);

        if (delegated is null || string.IsNullOrWhiteSpace(delegated.AccessToken))
            return null;

        TimeSpan lifetime = TimeSpan.FromSeconds(delegated.ExpiresIn) - RefreshWindow;

        if (lifetime > TimeSpan.Zero)
            cache.Set(cacheKey, delegated.AccessToken, lifetime);

        return delegated.AccessToken;
    }

    private async Task<CloudLoginTokenResponse?> ExchangeAsync(
        string subjectToken,
        string audience,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = httpClientFactory.CreateClient(CloudLoginTokenClientOptions.HttpClientName);

            using HttpRequestMessage request = new(
                HttpMethod.Post,
                $"{_options.Authority.TrimEnd('/')}/CloudLogin/Token/Exchange")
            {
                Content = JsonContent.Create(new CloudLoginExchangeRequest
                {
                    SubjectToken = subjectToken,
                    Audience = audience
                })
            };

            // The service credential proves which service is asking; the subject token
            // proves on whose behalf. The authority requires both, so neither a stolen
            // secret nor a stolen user token is enough on its own.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<CloudLoginTokenResponse>(cancellationToken);

            logger.LogWarning(
                "CloudLogin token exchange for audience {Audience} failed with {Status}. Check that this application is registered as a service client permitted to request that audience.",
                audience,
                response.StatusCode);

            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "CloudLogin token exchange for audience {Audience} failed.", audience);
            return null;
        }
    }

    /// <summary>
    /// Keys the delegation cache by a hash of the subject token, never the token itself:
    /// cache keys surface in diagnostics and memory dumps, and a bearer token in either
    /// is a credential in the clear.
    /// </summary>
    private static string DelegationCacheKey(string subjectToken, string audience) =>
        $"cloudlogin.delegated:{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(subjectToken)))}:{audience}";

    private async Task<CloudLoginTokenResponse?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = httpClientFactory.CreateClient(CloudLoginTokenClientOptions.HttpClientName);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"{_options.Authority.TrimEnd('/')}/CloudLogin/Token/Refresh",
                new CloudLoginRefreshRequest { RefreshToken = refreshToken },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CloudLoginTokenResponse>(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "CloudLogin token refresh call failed.");
            return null;
        }
    }
}

/// <summary>
/// Attaches the current user's access token to every outbound request on the
/// HttpClient it is registered against, minted for the audience of the service
/// being called.
/// <para>
/// This is the whole point of the design from a developer's perspective: calling a
/// downstream API is an ordinary typed-client call, and identity travels with it
/// automatically. Nothing in application code passes, or can pass, a user id.
/// </para>
/// </summary>
public sealed class CloudLoginTokenHandler(
    ICloudLoginTokenProvider tokenProvider,
    IOptions<CloudLoginTokenClientOptions> options,
    string? audience = null) : DelegatingHandler
{
    /// <summary>
    /// Overrides the target audience for one request. Set it when a single client talks
    /// to more than one service, which a per-client audience cannot express.
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> AudienceOption = new("CloudLogin.Audience");

    private readonly CloudLoginTokenClientOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? target = ResolveAudience(request);
        string? token = await tokenProvider.GetAccessTokenAsync(target, forceRefresh: false, cancellationToken);

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Most explicit wins: a per-request override, then the audience this client was
    /// registered with, then the downstream service whose base address matches the
    /// request. Falling through all three means the call is to this application's own
    /// audience.
    /// </summary>
    private string? ResolveAudience(HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(AudienceOption, out string? perRequest) &&
            !string.IsNullOrWhiteSpace(perRequest))
            return perRequest;

        if (!string.IsNullOrWhiteSpace(audience))
            return audience;

        return _options.ResolveDownstreamAudience(request.RequestUri);
    }
}

/// <summary>A downstream service this application calls on the user's behalf.</summary>
public sealed class CloudLoginDownstreamService
{
    /// <summary>The audience the downstream service validates its tokens against.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The downstream service's base address. Requests to that origin are automatically
    /// given a token for <see cref="Audience"/>, so wiring a client to a service is
    /// enough &mdash; no call site has to name an audience.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Relying-party configuration for talking to the authority.</summary>
public sealed class CloudLoginTokenClientOptions
{
    internal const string HttpClientName = "CloudLogin.Token";

    /// <summary>The authority's public HTTPS origin.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// This relying party's audience, used both when requesting tokens and when
    /// validating the ones it receives.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Requires an authenticated user on every endpoint that does not explicitly
    /// allow anonymous access.
    /// <para>
    /// Appropriate for API-only hosts, where failing closed on a forgotten
    /// <c>[Authorize]</c> is exactly what you want. Leave it off for a host that also
    /// serves anonymous pages or the sign-in callback, since the policy applies to
    /// those endpoints too, and mark its controllers with <c>[Authorize]</c> instead.
    /// </para>
    /// </summary>
    public bool RequireAuthenticatedByDefault { get; set; }

    /// <summary>Service client id registered with the authority.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Service client secret. Supply it from configuration or a secret store; it
    /// authenticates this service to the authority and must never ship in source.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Services this application calls on the user's behalf.
    /// <para>
    /// Populated by the Aspire integration from the references already declared in the
    /// AppHost, so the mapping follows from the topology rather than being a second
    /// place to keep in step with it.
    /// </para>
    /// </summary>
    public IList<CloudLoginDownstreamService> DownstreamServices { get; set; } = [];

    /// <summary>
    /// The audience registered for the service that <paramref name="requestUri"/>
    /// addresses, or <see langword="null"/> when the request is not bound for a
    /// registered downstream service.
    /// </summary>
    internal string? ResolveDownstreamAudience(Uri? requestUri)
    {
        if (requestUri is null || !requestUri.IsAbsoluteUri || DownstreamServices.Count == 0)
            return null;

        foreach (CloudLoginDownstreamService service in DownstreamServices)
        {
            if (string.IsNullOrWhiteSpace(service.Audience) ||
                !Uri.TryCreate(service.BaseUrl, UriKind.Absolute, out Uri? baseUri))
                continue;

            // Compare origins, not string prefixes: a prefix match would hand this
            // user's token to any host whose URL merely starts with the same text.
            if (Uri.Compare(
                    requestUri,
                    baseUri,
                    UriComponents.SchemeAndServer,
                    UriFormat.UriEscaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
                return service.Audience;
        }

        return null;
    }
}
