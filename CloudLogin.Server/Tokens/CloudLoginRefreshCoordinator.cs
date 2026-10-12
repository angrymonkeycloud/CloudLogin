using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Server.Tokens;

public enum CloudLoginRefreshStatuses
{
    Refreshed,
    Rejected,
    Unavailable
}

public sealed record CloudLoginRefreshedTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresOn);

public sealed record CloudLoginRefreshOutcome(CloudLoginRefreshStatuses Status, CloudLoginRefreshedTokens? Tokens = null, bool FromEarlierRefresh = false)
{
    public static CloudLoginRefreshOutcome Rejected { get; } = new(CloudLoginRefreshStatuses.Rejected);
    public static CloudLoginRefreshOutcome Unavailable { get; } = new(CloudLoginRefreshStatuses.Unavailable);
    public static CloudLoginRefreshOutcome Refreshed(CloudLoginRefreshedTokens tokens, bool fromEarlierRefresh = false) => new(CloudLoginRefreshStatuses.Refreshed, tokens, fromEarlierRefresh);
}

/// <summary>
/// Spends each refresh token once. The authority treats a second presentation of a spent token as theft and ends the whole session, so
/// requests that need a refresh at the same moment share one, and a request that still carries a token another request already rotated
/// is given that rotation instead of presenting the spent token again.
/// </summary>
public sealed class CloudLoginRefreshCoordinator(IDistributedCache cache, IDataProtectionProvider dataProtection, ILogger<CloudLoginRefreshCoordinator> logger)
{
    private static readonly TimeSpan MinimumRetention = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumRetention = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, Lazy<Task<CloudLoginRefreshOutcome>>> _inFlight = new(StringComparer.Ordinal);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AngryMonkey.CloudLogin.RefreshResults.v1");

    public async Task<CloudLoginRefreshOutcome> RefreshAsync(string refreshToken, Func<string, CancellationToken, Task<CloudLoginRefreshOutcome>> refresh, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentNullException.ThrowIfNull(refresh);

        string key = Key(refreshToken);

        if (await ReadAsync(key, cancellationToken) is { } earlier)
            return CloudLoginRefreshOutcome.Refreshed(earlier, fromEarlierRefresh: true);

        Lazy<Task<CloudLoginRefreshOutcome>> run = _inFlight.GetOrAdd(key, _ => new Lazy<Task<CloudLoginRefreshOutcome>>(() => RunAsync(key, refreshToken, refresh)));

        return await run.Value.WaitAsync(cancellationToken);
    }

    internal int InFlightCount => _inFlight.Count;

    private async Task<CloudLoginRefreshOutcome> RunAsync(string key, string refreshToken, Func<string, CancellationToken, Task<CloudLoginRefreshOutcome>> refresh)
    {
        try
        {
            if (await ReadAsync(key, CancellationToken.None) is { } earlier)
                return CloudLoginRefreshOutcome.Refreshed(earlier, fromEarlierRefresh: true);

            using CancellationTokenSource timeout = new(RefreshTimeout);
            CloudLoginRefreshOutcome outcome;

            try
            {
                outcome = await refresh(refreshToken, timeout.Token);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "A refresh failed before the authority answered; the session is kept.");
                outcome = CloudLoginRefreshOutcome.Unavailable;
            }

            if (outcome is { Status: CloudLoginRefreshStatuses.Refreshed, Tokens: { } tokens })
                await WriteAsync(key, tokens);

            return outcome;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<CloudLoginRefreshedTokens?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? stored = await cache.GetAsync(key, cancellationToken);

            if (stored is null)
                return null;

            CloudLoginRefreshedTokens? tokens = JsonSerializer.Deserialize<CloudLoginRefreshedTokens>(_protector.Unprotect(stored));

            return tokens is { AccessToken.Length: > 0, RefreshToken.Length: > 0 } ? tokens : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            logger.LogWarning(exception, "A shared refresh result could not be read; the refresh runs again.");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The shared refresh cache could not be reached; the refresh is coordinated on this instance only.");
            return null;
        }
    }

    private async Task WriteAsync(string key, CloudLoginRefreshedTokens tokens)
    {
        TimeSpan remaining = tokens.ExpiresOn - DateTimeOffset.UtcNow;
        TimeSpan retention = remaining < MinimumRetention ? MinimumRetention : remaining > MaximumRetention ? MaximumRetention : remaining;

        try
        {
            byte[] protectedTokens = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(tokens));
            await cache.SetAsync(key, protectedTokens, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = retention });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "A refresh result could not be shared; another instance holding the same token may refresh it again.");
        }
    }

    internal static string Key(string refreshToken) =>
        $"cloudlogin.refresh-result:{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)))}";
}
