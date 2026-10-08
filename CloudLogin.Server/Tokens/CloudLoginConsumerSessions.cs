using System.Security.Claims;
using AngryMonkey.CloudLogin.Server.Core.Application;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AngryMonkey.CloudLogin.Server.Tokens;

public static class CloudLoginSessionClaims
{
    public const string SessionId = "cloudlogin:sid";
    public const string IssuedAt = "cloudlogin:iat";
}

public interface ICloudLoginSessionRevocations
{
    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RevokeSubjectAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<bool> IsRevokedAsync(string? sessionId, string? subject, DateTimeOffset issuedAt, CancellationToken cancellationToken = default);

    /// <summary>Whether a logout token has already been applied. A token is marked only after its revocation was stored.</summary>
    Task<bool> IsLogoutTokenProcessedAsync(string tokenId, CancellationToken cancellationToken = default);

    Task MarkLogoutTokenProcessedAsync(string tokenId, DateTimeOffset expiresOn, CancellationToken cancellationToken = default);

    Task<bool> IsStatusCheckDueAsync(string sessionId, TimeSpan interval, CancellationToken cancellationToken = default);
}

public sealed class DistributedCacheSessionRevocations(IDistributedCache cache, CloudLoginServerConfiguration? configuration = null) : ICloudLoginSessionRevocations
{
    private TimeSpan Retention => (configuration?.SessionDuration ?? TimeSpan.FromHours(8)) + TimeSpan.FromHours(1);

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        cache.SetStringAsync(Key("sid", sessionId), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), Options(Retention), cancellationToken);

    public async Task RevokeSubjectAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        string key = Key("sub", subject);
        long requested = at.ToUnixTimeMilliseconds();

        // The cache has no compare-and-set, so write, read back, and write again if a slower writer put an older value over this one.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            long existing = long.TryParse(await cache.GetStringAsync(key, cancellationToken), out long value) ? value : 0;

            if (existing >= requested)
                return;

            await cache.SetStringAsync(key, requested.ToString(), Options(Retention), cancellationToken);
        }
    }

    public async Task<bool> IsRevokedAsync(string? sessionId, string? subject, DateTimeOffset issuedAt, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(sessionId) && await cache.GetStringAsync(Key("sid", sessionId), cancellationToken) is not null)
            return true;

        return !string.IsNullOrEmpty(subject)
            && long.TryParse(await cache.GetStringAsync(Key("sub", subject), cancellationToken), out long revokedAt)
            && issuedAt.ToUnixTimeMilliseconds() <= revokedAt;
    }

    public async Task<bool> IsLogoutTokenProcessedAsync(string tokenId, CancellationToken cancellationToken = default) =>
        await cache.GetStringAsync(Key("jti", tokenId), cancellationToken) is not null;

    public Task MarkLogoutTokenProcessedAsync(string tokenId, DateTimeOffset expiresOn, CancellationToken cancellationToken = default) =>
        cache.SetStringAsync(Key("jti", tokenId), "1", Options(TimeSpan.FromMinutes(15) + (expiresOn > DateTimeOffset.UtcNow ? expiresOn - DateTimeOffset.UtcNow : TimeSpan.Zero)), cancellationToken);

    public async Task<bool> IsStatusCheckDueAsync(string sessionId, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        string key = Key("checked", sessionId);

        if (await cache.GetStringAsync(key, cancellationToken) is not null)
            return false;

        await cache.SetStringAsync(key, "1", Options(interval), cancellationToken);
        return true;
    }

    private static DistributedCacheEntryOptions Options(TimeSpan lifetime) => new() { AbsoluteExpirationRelativeToNow = lifetime };

    private static string Key(string kind, string value) => $"cloudlogin:{kind}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32]}";
}

public sealed record LogoutTokenResult(bool Valid, string? SessionId, string? Subject, string? TokenId, DateTimeOffset ExpiresOn, string? Failure, DateTimeOffset? RevokedBefore = null);

public sealed class CloudLoginLogoutTokenValidator(IOptions<CloudLoginTokenClientOptions> options, IHttpClientFactory httpClientFactory)
{
    public const string EventType = "http://schemas.openid.net/event/backchannel-logout";
    public const string TokenType = "logout+jwt";

    private readonly CloudLoginTokenClientOptions _options = options.Value;
    private ConfigurationManager<OpenIdConnectConfiguration>? _configuration;

    public async Task<LogoutTokenResult> ValidateAsync(string logoutToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Authority) || string.IsNullOrWhiteSpace(_options.ClientId))
            return Fail("NotConfigured");

        JsonWebToken token;

        try
        {
            token = new JsonWebToken(logoutToken);
        }
        catch (ArgumentException)
        {
            return Fail("Malformed");
        }

        if (!string.Equals(token.Typ, TokenType, StringComparison.Ordinal))
            return Fail("WrongTokenType");

        OpenIdConnectConfiguration metadata;

        try
        {
            metadata = await Manager().GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail("MetadataUnavailable");
        }

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Authority.TrimEnd('/'),
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKeys = metadata.SigningKeys,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        });

        if (!result.IsValid)
            return Fail("InvalidSignatureOrClaims");

        DateTimeOffset issuedAt = new(token.IssuedAt, TimeSpan.Zero);
        DateTimeOffset expires = new(token.ValidTo, TimeSpan.Zero);

        if (token.IssuedAt == DateTime.MinValue || expires - issuedAt > TimeSpan.FromMinutes(10) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return Fail("BadTimestamps");

        if (!token.TryGetClaim("events", out Claim? events) || !events.Value.Contains(EventType, StringComparison.Ordinal))
            return Fail("MissingEvent");

        if (token.TryGetClaim("nonce", out _))
            return Fail("NoncePresent");

        string? sessionId = token.TryGetClaim("sid", out Claim? sid) ? sid.Value : null;
        string? subject = token.Subject;

        if (string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(subject))
            return Fail("NoSessionOrSubject");

        if (string.IsNullOrWhiteSpace(token.Id))
            return Fail("MissingTokenId");

        DateTimeOffset? revokedBefore = null;

        if (token.TryGetClaim(LogoutTokenClaims.RevokedBefore, out Claim? stamp))
        {
            if (!long.TryParse(stamp.Value, out long milliseconds))
                return Fail("BadTimestamps");

            revokedBefore = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

            if (revokedBefore > DateTimeOffset.UtcNow.AddMinutes(2))
                return Fail("BadTimestamps");
        }

        return new LogoutTokenResult(true, sessionId, subject, token.Id, expires, null, revokedBefore);
    }

    private static LogoutTokenResult Fail(string reason) => new(false, null, null, null, default, reason);

    private ConfigurationManager<OpenIdConnectConfiguration> Manager()
    {
        if (_configuration is not null)
            return _configuration;

        string authority = _options.Authority.TrimEnd('/');
        bool loopback = Uri.TryCreate(authority, UriKind.Absolute, out Uri? uri) && uri.IsLoopback;

        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpClientFactory.CreateClient(CloudLoginTokenClientOptions.HttpClientName)) { RequireHttps = !loopback });

        return _configuration;
    }
}

public static class CloudLoginSessionValidation
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        IServiceProvider services = context.HttpContext.RequestServices;
        CloudLoginServerConfiguration? configuration = services.GetService<CloudLoginServerConfiguration>();
        ICloudLoginSessionRevocations? revocations = services.GetService<ICloudLoginSessionRevocations>();

        if (revocations is null)
            return;

        string? sessionId = context.Principal?.FindFirst(CloudLoginSessionClaims.SessionId)?.Value;
        string? issuedAtRaw = context.Principal?.FindFirst(CloudLoginSessionClaims.IssuedAt)?.Value;

        if (string.IsNullOrEmpty(sessionId) || !long.TryParse(issuedAtRaw, out long issuedAtSeconds))
        {
            if (configuration?.RequireSessionBinding == true)
                await Reject(context);

            return;
        }

        string? subject = context.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(issuedAtSeconds);

        if (await revocations.IsRevokedAsync(sessionId, subject, issuedAt, context.HttpContext.RequestAborted))
        {
            await Reject(context);
            return;
        }

        CloudLoginTokenClientOptions? options = services.GetService<IOptions<CloudLoginTokenClientOptions>>()?.Value;
        ICloudLoginClientCredentials? credentials = services.GetService<ICloudLoginClientCredentials>();

        if (options is null || credentials is not { IsConfigured: true } || string.IsNullOrWhiteSpace(options.Authority) || options.SessionRevalidationInterval <= TimeSpan.Zero)
            return;

        if (!await revocations.IsStatusCheckDueAsync(sessionId, options.SessionRevalidationInterval, context.HttpContext.RequestAborted))
            return;

        bool? active = await QueryStatusAsync(services, options, credentials, sessionId, context.HttpContext.RequestAborted);

        if (active == false)
        {
            await revocations.RevokeSessionAsync(sessionId, context.HttpContext.RequestAborted);
            await Reject(context);
        }
    }

    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static async Task<bool?> QueryStatusAsync(IServiceProvider services, CloudLoginTokenClientOptions options, ICloudLoginClientCredentials credentials, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(CloudLoginTokenClientOptions.HttpClientName);

            using HttpRequestMessage request = new(HttpMethod.Post, $"{options.Authority.TrimEnd('/')}/CloudLogin/Token/SessionStatus")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { sessionId })
            };

            await credentials.ApplyAsync(request, cancellationToken);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            using System.Text.Json.JsonDocument body = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return body.RootElement.TryGetProperty("active", out System.Text.Json.JsonElement active) ? active.GetBoolean() : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            services.GetService<ILoggerFactory>()?.CreateLogger("CloudLogin.Session").LogWarning(exception, "The authority could not be asked whether a session is still active.");
            return null;
        }
    }
}
