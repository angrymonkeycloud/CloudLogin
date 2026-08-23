using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Supplies a valid access token for the current user, refreshing it when it is
/// close to expiry.
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
    /// A currently valid access token for the signed-in user, or <see langword="null"/>
    /// when the request is anonymous or the session can no longer be refreshed.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CloudLoginTokenProvider(
    IHttpContextAccessor accessor,
    IHttpClientFactory httpClientFactory,
    IOptions<CloudLoginTokenClientOptions> options,
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

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
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
/// HttpClient it is registered against.
/// <para>
/// This is the whole point of the design from a developer's perspective: calling a
/// downstream API is an ordinary typed-client call, and identity travels with it
/// automatically. Nothing in application code passes, or can pass, a user id.
/// </para>
/// </summary>
public sealed class CloudLoginTokenHandler(ICloudLoginTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? token = await tokenProvider.GetAccessTokenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
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
}
