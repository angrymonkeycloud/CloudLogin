using System.Collections.Concurrent;
using System.Threading.Channels;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public interface ILogoutNotifier
{
    Task NotifyFamilyAsync(SessionFamilyDocument family, CancellationToken cancellationToken = default);

    Task NotifyUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class BackChannelLogoutOptions
{
    public int MaxAttempts { get; set; } = 4;

    public TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Delivers inside the call that revoked the session, with no queue. For hosts that have no background workers, and for tests.</summary>
    public bool DeliverInline { get; set; }
}

public sealed record LogoutDelivery(string ClientId, Uri Endpoint, string Token, Guid UserId, string? SessionId);

public sealed class BackChannelLogoutService(
    IClientDirectory clients,
    ISessionRepository sessions,
    IServiceProvider services,
    IOptions<CloudLoginTokenOptions> tokenOptions,
    IHttpClientFactory httpClientFactory,
    IAuditLogger audit,
    ILogger<BackChannelLogoutService> logger,
    IOptions<BackChannelLogoutOptions>? options = null) : ILogoutNotifier
{
    private readonly BackChannelLogoutOptions _options = options?.Value ?? new BackChannelLogoutOptions();
    private readonly Channel<LogoutDelivery> _queue = Channel.CreateUnbounded<LogoutDelivery>();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recent = new(StringComparer.Ordinal);
    private int _pending;
    private long _delivered;
    private long _failed;

    public ChannelReader<LogoutDelivery> Deliveries => _queue.Reader;

    public int Pending => Volatile.Read(ref _pending);

    public long Delivered => Interlocked.Read(ref _delivered);

    public long Failed => Interlocked.Read(ref _failed);

    public async Task NotifyFamilyAsync(SessionFamilyDocument family, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(family.UserId, out Guid userId))
            return;

        HashSet<string> applications = [];

        // A browser session at the authority tells every application signed in from it; an application's own session tells that application.
        if (ApplicationOf(family) is { } application)
            applications.Add(application);
        else
            foreach (SessionFamilyDocument member in await sessions.FindFamiliesBySessionIdAsync(family.SessionId, cancellationToken))
                if (ApplicationOf(member) is { } memberApplication)
                    applications.Add(memberApplication);

        foreach (string clientId in applications)
            await EnqueueAsync(clientId, userId, family.SessionId, cancellationToken);
    }

    public async Task NotifyUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        HashSet<string> applications = [.. (await sessions.GetFamiliesForUserAsync(userId, cancellationToken))
            .Select(ApplicationOf)
            .Where(clientId => clientId is not null)!];

        foreach (string clientId in applications)
            await EnqueueAsync(clientId, userId, null, cancellationToken);
    }

    /// <summary>The application a session family belongs to, or null for the authority's own browser session.</summary>
    private static string? ApplicationOf(SessionFamilyDocument family)
    {
        string? id = string.IsNullOrEmpty(family.ClientId) ? family.Audience : family.ClientId;
        return string.IsNullOrEmpty(id) || string.Equals(id, SessionService.BrowserAudience, StringComparison.OrdinalIgnoreCase) ? null : id;
    }

    private async Task EnqueueAsync(string clientId, Guid userId, string? sessionId, CancellationToken cancellationToken)
    {
        string? uri = await clients.GetBackChannelLogoutUriAsync(clientId, cancellationToken);

        if (uri is not { Length: > 0 } || !ApplicationValidation.TryNormalizeUrl(uri, allowCustomScheme: false, out string normalized))
            return;

        string key = $"{clientId}|{sessionId ?? userId.ToString()}";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (sessionId is not null && _recent.TryGetValue(key, out DateTimeOffset last) && now - last < _options.DeduplicationWindow)
            return;

        if (_recent.Count > 5000)
            _recent.Clear();

        if (sessionId is not null)
            _recent[key] = now;

        LogoutDelivery delivery = new(clientId, new Uri(normalized), await CreateTokenAsync(clientId, userId, sessionId, cancellationToken), userId, sessionId);

        Interlocked.Increment(ref _pending);

        if (_options.DeliverInline)
            await DeliverAsync(delivery, cancellationToken);
        else
            await _queue.Writer.WriteAsync(delivery, cancellationToken);
    }

    public async Task DeliverAsync(LogoutDelivery delivery, CancellationToken cancellationToken)
    {
        int attempts = 0;
        string? lastFailure = null;

        try
        {
            while (attempts < Math.Max(1, _options.MaxAttempts))
            {
                if (attempts > 0)
                    await Task.Delay(_options.RetryDelays.Length == 0 ? TimeSpan.Zero : _options.RetryDelays[Math.Min(attempts - 1, _options.RetryDelays.Length - 1)], cancellationToken);

                attempts++;

                try
                {
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_options.AttemptTimeout);

                    HttpClient client = httpClientFactory.CreateClient(BackChannelLogoutDefaults.HttpClientName);

                    using HttpRequestMessage request = new(HttpMethod.Post, delivery.Endpoint)
                    {
                        Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = delivery.Token })
                    };

                    using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref _delivered);
                        await RecordAsync(AuditEventTypes.LogoutNotified, AuditResults.Success, delivery, attempts, null, cancellationToken);
                        return;
                    }

                    lastFailure = $"HTTP {(int)response.StatusCode}";
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    lastFailure = exception.GetType().Name;
                }
            }

            Interlocked.Increment(ref _failed);
            logger.LogError("Back-channel logout to client {ClientId} failed after {Attempts} attempts ({Failure}). Its local sessions end at the next session revalidation.", delivery.ClientId, attempts, lastFailure);
            await RecordAsync(AuditEventTypes.LogoutDeliveryFailed, AuditResults.Failure, delivery, attempts, lastFailure, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    public async Task WaitForIdleAsync(TimeSpan timeout)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow + timeout;

        while (Pending > 0 && DateTimeOffset.UtcNow < until)
            await Task.Delay(10);
    }

    private Task RecordAsync(string eventType, string result, LogoutDelivery delivery, int attempts, string? failure, CancellationToken cancellationToken) =>
        audit.LogAsync(new AuditEntry
        {
            EventType = eventType,
            UserId = delivery.UserId,
            ClientId = delivery.ClientId,
            Result = result,
            Data = new Dictionary<string, string>
            {
                ["Attempts"] = attempts.ToString(),
                ["Scope"] = delivery.SessionId is null ? "User" : "Session",
                ["Failure"] = failure ?? string.Empty
            }
        }, cancellationToken);

    private async Task<string> CreateTokenAsync(string clientId, Guid userId, string? sessionId, CancellationToken cancellationToken)
    {
        CloudLoginSigningKeyManager keys = services.GetRequiredService<CloudLoginSigningKeyManager>();
        (Microsoft.IdentityModel.Tokens.SigningCredentials credentials, _) = await keys.GetSigningCredentialsAsync(cancellationToken);
        DateTime now = DateTime.UtcNow;

        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            ["sub"] = userId.ToString(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            [LogoutTokenClaims.RevokedBefore] = new DateTimeOffset(now, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            ["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() }
        };

        if (!string.IsNullOrEmpty(sessionId))
            claims["sid"] = sessionId;

        return new JsonWebTokenHandler().CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = tokenOptions.Value.Issuer.TrimEnd('/'),
            Audience = clientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_options.TokenLifetime),
            SigningCredentials = credentials,
            Claims = claims,
            TokenType = "logout+jwt"
        });
    }
}

/// <summary>Claims in a logout token beyond the standard ones.</summary>
public static class LogoutTokenClaims
{
    /// <summary>Unix milliseconds on the authority's clock: every session of the subject issued at or before this is ended. Makes applying a subject-level notification the same however often it runs.</summary>
    public const string RevokedBefore = "revoked_before_ms";
}

public static class BackChannelLogoutDefaults
{
    public const string HttpClientName = "CloudLogin.BackChannelLogout";
}

public sealed class BackChannelLogoutWorker(BackChannelLogoutService service) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (LogoutDelivery delivery in service.Deliveries.ReadAllAsync(stoppingToken))
            _ = Task.Run(() => service.DeliverAsync(delivery, stoppingToken), stoppingToken);
    }
}
