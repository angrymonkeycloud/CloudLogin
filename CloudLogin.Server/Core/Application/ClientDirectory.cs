using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public enum ClientAuthenticationFailures
{
    None,
    InvalidClientId,
    InvalidSecret,
    Blocked,
    RegistryUnavailable
}

/// <summary>
/// Who is calling. A backend names itself and proves it holds a secret key; a website without a backend or a native app is identified by
/// its own origin, so a token it receives is only valid there (<see cref="Audience"/> is that origin).
/// </summary>
public sealed record ClientIdentity(string ClientId, ApplicationKinds Kind, string? SecretKeyId = null)
{
    public bool IsPublic => Kind != ApplicationKinds.Backend;

    /// <summary>The audience of the tokens it receives. For a public client this is fixed to its own origin.</summary>
    public string Audience => ClientId;

    /// <summary>A website without a backend keeps nothing safe from scripts, so it gets no refresh token. A native app does.</summary>
    public bool ReceivesRefreshTokens => Kind != ApplicationKinds.Website;
}

public sealed record ClientAuthenticationResult(ClientIdentity? Client, ClientAuthenticationFailures Failure)
{
    public bool Succeeded => Client is not null;
}

public interface IClientDirectory
{
    /// <summary>A backend: any name it gives itself, with any usable secret key.</summary>
    Task<ClientAuthenticationResult> AuthenticateAsync(string clientId, string secret, CancellationToken cancellationToken = default);

    /// <summary>A website without a backend or a native app, from its redirect URI or its id (its origin).</summary>
    Task<ClientAuthenticationResult> AuthenticatePublicAsync(string redirectUriOrClientId, CancellationToken cancellationToken = default);

    /// <summary>Records where a backend runs and where it wants back-channel logout notifications.</summary>
    Task RecordBackendAsync(string clientId, string origin, string? backChannelLogoutUri, CancellationToken cancellationToken = default);

    Task<string?> GetBackChannelLogoutUriAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>Whether tokens may be issued to this application. Unknown while the registry is unreadable counts as no.</summary>
    Task<bool> IsAllowedAsync(string clientId, CancellationToken cancellationToken = default);

    void Invalidate();
}

public static class ClientIds
{
    private static readonly HashSet<string> NeverClientSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "data", "file", "blob", "vbscript", "about", "ftp", "ws", "wss"
    };

    /// <summary>A name a backend gives itself: letters, digits, '-', '_' and '.', so it can never look like an origin.</summary>
    public static bool IsBackendName(string? value) =>
        value is { Length: >= 1 and <= 64 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    /// <summary>
    /// The identity of a public client: <c>https://host[:port]</c> for a website (http only on loopback), <c>scheme:</c> for a native app.
    /// Accepts a redirect URI or an id already in that form.
    /// </summary>
    public static bool TryPublicIdentity(string? value, out string clientId, out ApplicationKinds kind)
    {
        clientId = string.Empty;
        kind = ApplicationKinds.Website;

        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) || value.Contains('\\'))
            return false;

        if (value.EndsWith(':') && IsScheme(value[..^1]) && !IsWebScheme(value[..^1]) && !NeverClientSchemes.Contains(value[..^1]))
        {
            clientId = value.ToLowerInvariant();
            kind = ApplicationKinds.NativeApp;
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !string.IsNullOrEmpty(uri.UserInfo) || NeverClientSchemes.Contains(uri.Scheme))
            return false;

        if (IsWebScheme(uri.Scheme))
        {
            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
                return false;

            clientId = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            return true;
        }

        clientId = $"{uri.Scheme.ToLowerInvariant()}:";
        kind = ApplicationKinds.NativeApp;
        return true;
    }

    /// <summary>A URL a backend may use as a return or post-logout destination: https, or http on loopback, with no credentials.</summary>
    public static bool IsBackendUrl(string? value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    public static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();

    private static bool IsWebScheme(string scheme) => scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static bool IsScheme(string value) =>
        value.Length is >= 2 and <= 64 && char.IsAsciiLetter(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '+' or '.' or '-');
}

public sealed class ClientDirectory(
    IOptions<CloudLoginTokenOptions> options,
    IApplicationRepository? applications = null,
    ISecretKeyRepository? secretKeys = null,
    IAuditLogger? audit = null,
    ILogger<ClientDirectory>? logger = null,
    TimeProvider? clock = null) : IClientDirectory
{
    public const string DeploymentKeyPrefix = "deployment-";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureAuditInterval = TimeSpan.FromMinutes(1);

    private readonly CloudLoginTokenOptions _options = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private DateTimeOffset Now => _clock.GetUtcNow();
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, ApplicationDocument? Application)> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, ApplicationDocument? Application)> _known = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failureAudited = new(StringComparer.Ordinal);
    private (DateTimeOffset At, List<SecretKeyDocument> Keys)? _keys;
    private (DateTimeOffset At, List<SecretKeyDocument> Keys)? _knownKeys;

    public static string DeploymentKeyId(string secretHash) =>
        DeploymentKeyPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secretHash)))[..12];

    /// <summary>
    /// Forgets everything read, including what an outage would fall back on: after an administrator's change, the state from before it must
    /// never be served again, so an outage that follows fails closed instead.
    /// </summary>
    public void Invalidate()
    {
        _cache.Clear();
        _known.Clear();
        _keys = null;
        _knownKeys = null;
    }

    public async Task<ClientAuthenticationResult> AuthenticateAsync(string clientId, string secret, CancellationToken cancellationToken = default)
    {
        ClientAuthenticationResult result = await AuthenticateBackendAsync(clientId, secret, cancellationToken);

        if (!result.Succeeded)
            await AuditFailureAsync(clientId, result.Failure, cancellationToken);

        return result;
    }

    private async Task<ClientAuthenticationResult> AuthenticateBackendAsync(string clientId, string secret, CancellationToken cancellationToken)
    {
        if (!ClientIds.IsBackendName(clientId))
            return new(null, ClientAuthenticationFailures.InvalidClientId);

        if (string.IsNullOrWhiteSpace(secret))
            return new(null, ClientAuthenticationFailures.InvalidSecret);

        DateTimeOffset now = Now;
        string hash = CloudLoginTokenOptions.HashSecret(secret);
        (List<SecretKeyDocument>? keys, bool keysKnown) = await GetKeysAsync(cancellationToken);
        string? keyId = null;

        foreach (SecretKeyDocument key in keys ?? [])
            if (!key.IsDeployment && key.IsUsableAt(now) && Matches(key.SecretHash, hash))
                keyId = key.Id;

        if (keyId is null)
        {
            string? deploymentHash = _options.SecretKeyHashes.FirstOrDefault(candidate => Matches(candidate, hash));

            if (deploymentHash is null)
                return new(null, ClientAuthenticationFailures.InvalidSecret);

            string deploymentId = DeploymentKeyId(deploymentHash);

            if (!keysKnown && !_options.AllowDeploymentKeysWithoutRegistry)
                return new(null, ClientAuthenticationFailures.RegistryUnavailable);

            if (keys?.FirstOrDefault(key => key.Id == deploymentId) is { } record && !record.IsUsableAt(now))
                return new(null, ClientAuthenticationFailures.InvalidSecret);

            keyId = deploymentId;
        }

        ClientAuthenticationFailures blocked = await BlockedAsync(clientId, cancellationToken);

        if (blocked != ClientAuthenticationFailures.None)
            return new(null, blocked);

        ClientIdentity identity = new(clientId, ApplicationKinds.Backend, keyId);
        await TouchAsync(identity, null, null, now, cancellationToken);
        await TouchKeyAsync(keyId, clientId, keys, now, cancellationToken);
        return new(identity, ClientAuthenticationFailures.None);
    }

    public async Task<ClientAuthenticationResult> AuthenticatePublicAsync(string redirectUriOrClientId, CancellationToken cancellationToken = default)
    {
        if (!ClientIds.TryPublicIdentity(redirectUriOrClientId, out string clientId, out ApplicationKinds kind))
            return new(null, ClientAuthenticationFailures.InvalidClientId);

        ClientAuthenticationFailures blocked = await BlockedAsync(clientId, cancellationToken);

        if (blocked != ClientAuthenticationFailures.None)
        {
            await AuditFailureAsync(clientId, blocked, cancellationToken);
            return new(null, blocked);
        }

        ClientIdentity identity = new(clientId, kind);
        await TouchAsync(identity, kind == ApplicationKinds.Website ? clientId : null, null, Now, cancellationToken);
        return new(identity, ClientAuthenticationFailures.None);
    }

    public async Task RecordBackendAsync(string clientId, string origin, string? backChannelLogoutUri, CancellationToken cancellationToken = default)
    {
        if (ClientIds.IsBackendName(clientId))
            await TouchAsync(new ClientIdentity(clientId, ApplicationKinds.Backend), origin, backChannelLogoutUri, Now, cancellationToken, force: true);
    }

    public async Task<string?> GetBackChannelLogoutUriAsync(string clientId, CancellationToken cancellationToken = default) =>
        (await GetApplicationAsync(clientId, cancellationToken)).Document?.BackChannelLogoutUri;

    public async Task<bool> IsAllowedAsync(string clientId, CancellationToken cancellationToken = default) =>
        await BlockedAsync(clientId, cancellationToken) == ClientAuthenticationFailures.None;

    private async Task<ClientAuthenticationFailures> BlockedAsync(string clientId, CancellationToken cancellationToken)
    {
        (ApplicationDocument? application, bool unavailable) = await GetApplicationAsync(clientId, cancellationToken);

        if (unavailable && !_options.AllowDeploymentKeysWithoutRegistry)
            return ClientAuthenticationFailures.RegistryUnavailable;

        return application?.IsBlocked == true ? ClientAuthenticationFailures.Blocked : ClientAuthenticationFailures.None;
    }

    /// <summary>Records that an application was seen. Best effort: a sign-in never fails because the record could not be written.</summary>
    private async Task TouchAsync(ClientIdentity identity, string? origin, string? backChannelLogoutUri, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        if (applications is null)
            return;

        try
        {
            ApplicationDocument? existing = (await GetApplicationAsync(identity.ClientId, cancellationToken)).Document;

            bool originKnown = origin is null || existing?.Origins.FirstOrDefault() == origin;
            bool backChannelKnown = backChannelLogoutUri is null || existing?.BackChannelLogoutUri == backChannelLogoutUri;
            bool keyKnown = identity.SecretKeyId is null || existing?.LastSecretKeyId == identity.SecretKeyId;

            if (existing is not null && !force && originKnown && backChannelKnown && keyKnown && now - existing.LastSeenOn < TouchInterval)
                return;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                ApplicationDocument? current = await applications.GetAsync(identity.ClientId, cancellationToken);

                if (current is null)
                {
                    ApplicationDocument created = new()
                    {
                        Id = identity.ClientId,
                        ClientId = identity.ClientId,
                        Kind = identity.Kind,
                        FirstSeenOn = now,
                        LastSeenOn = now,
                        LastSecretKeyId = identity.SecretKeyId,
                        BackChannelLogoutUri = backChannelLogoutUri,
                        Origins = origin is null ? [] : [origin]
                    };

                    try
                    {
                        await applications.CreateAsync(created, cancellationToken);
                        Remember(identity.ClientId, created);
                        return;
                    }
                    catch (CoreConflictException)
                    {
                        continue;
                    }
                }

                current.Kind = identity.Kind;
                current.LastSeenOn = now;

                if (identity.SecretKeyId is not null)
                    current.LastSecretKeyId = identity.SecretKeyId;

                if (backChannelLogoutUri is not null)
                    current.BackChannelLogoutUri = backChannelLogoutUri;

                if (origin is not null)
                {
                    current.Origins.Remove(origin);
                    current.Origins.Insert(0, origin);

                    if (current.Origins.Count > 10)
                        current.Origins.RemoveRange(10, current.Origins.Count - 10);
                }

                if (await applications.TryReplaceAsync(current, cancellationToken))
                {
                    Remember(identity.ClientId, current);
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Could not record activity for application {ClientId}.", identity.ClientId);
        }
    }

    private async Task TouchKeyAsync(string keyId, string clientId, List<SecretKeyDocument>? keys, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (secretKeys is null)
            return;

        SecretKeyDocument? key = keys?.FirstOrDefault(item => item.Id == keyId);

        if (key is not null && key.LastUsedOn is { } last && now - last < TouchInterval && key.LastUsedBy == clientId)
            return;

        try
        {
            if (key is null && keyId.StartsWith(DeploymentKeyPrefix, StringComparison.Ordinal))
            {
                // A deployment key gets a record the first time it is used, so it can be seen and revoked in the admin.
                string hash = _options.SecretKeyHashes.First(candidate => DeploymentKeyId(candidate) == keyId);

                await secretKeys.CreateAsync(new SecretKeyDocument
                {
                    Id = keyId,
                    Label = "Declared by the deployment",
                    SecretHash = hash,
                    IsDeployment = true,
                    CreatedOn = now,
                    LastUsedOn = now,
                    LastUsedBy = clientId
                }, cancellationToken);
            }
            else
            {
                SecretKeyDocument? current = await secretKeys.GetAsync(keyId, cancellationToken);

                if (current is null)
                    return;

                current.LastUsedOn = now;
                current.LastUsedBy = clientId;
                await secretKeys.TryReplaceAsync(current, cancellationToken);
            }

            _keys = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Could not record use of secret key {KeyId}.", keyId);
        }
    }

    private void Remember(string clientId, ApplicationDocument application)
    {
        DateTimeOffset now = Now;
        _cache[clientId] = (now, application);
        _known[clientId] = (now, application);
    }

    private bool Fresh(DateTimeOffset at) => Now - at <= _options.RegistryStaleTolerance;

    /// <summary>The application record, or the last one read when the registry fails; unavailable when nothing recent is known.</summary>
    private async Task<(ApplicationDocument? Document, bool Unavailable)> GetApplicationAsync(string clientId, CancellationToken cancellationToken)
    {
        if (applications is null)
            return (null, false);

        DateTimeOffset now = Now;

        if (_cache.TryGetValue(clientId, out (DateTimeOffset At, ApplicationDocument? Application) cached) && now - cached.At < CacheLifetime)
            return (cached.Application, false);

        try
        {
            ApplicationDocument? application = await applications.GetAsync(clientId, cancellationToken);

            if (_cache.Count > 1000)
                _cache.Clear();

            _cache[clientId] = (now, application);
            _known[clientId] = (now, application);
            return (application, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_known.TryGetValue(clientId, out (DateTimeOffset At, ApplicationDocument? Application) last) && Fresh(last.At))
            {
                logger?.LogError(exception, "The application registry could not be read for {ClientId}; enforcing what was read at {At}.", clientId, last.At);
                return (last.Application, false);
            }

            logger?.LogError(exception, "The application registry could not be read for {ClientId} and nothing recent is known. It is refused until it can be read.", clientId);
            return (null, true);
        }
    }

    /// <summary>The secret keys, or the last list read when the registry fails; not known when nothing recent was read.</summary>
    private async Task<(List<SecretKeyDocument>? Keys, bool Known)> GetKeysAsync(CancellationToken cancellationToken)
    {
        if (secretKeys is null)
            return ([], true);

        DateTimeOffset now = Now;

        if (_keys is { } cached && now - cached.At < CacheLifetime)
            return (cached.Keys, true);

        try
        {
            List<SecretKeyDocument> keys = await secretKeys.GetAllAsync(cancellationToken);
            _keys = (now, keys);
            _knownKeys = (now, keys);
            return (keys, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_knownKeys is { } last && Fresh(last.At))
            {
                logger?.LogError(exception, "The secret keys could not be read; enforcing the list read at {At}.", last.At);
                return (last.Keys, true);
            }

            logger?.LogError(exception, "The secret keys could not be read and nothing recent is known.");
            return (null, false);
        }
    }

    private static bool Matches(string storedHash, string presentedHash) =>
        !string.IsNullOrEmpty(storedHash) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(presentedHash));

    private async Task AuditFailureAsync(string clientId, ClientAuthenticationFailures failure, CancellationToken cancellationToken)
    {
        if (audit is null)
            return;

        string name = ClientIds.IsBackendName(clientId) || ClientIds.TryPublicIdentity(clientId, out _, out _) ? clientId : "*";
        string key = $"{name}|{failure}";
        DateTimeOffset now = Now;

        if (_failureAudited.TryGetValue(key, out DateTimeOffset last) && now - last < FailureAuditInterval)
            return;

        if (_failureAudited.Count > 1000)
            _failureAudited.Clear();

        _failureAudited[key] = now;

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.ClientAuthenticationFailed,
            ClientId = name == "*" ? null : name,
            Result = AuditResults.Failure,
            Data = new Dictionary<string, string> { ["Reason"] = failure.ToString() }
        }, cancellationToken);
    }
}

public static class ClientDirectoryServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLoginClientDirectory(this IServiceCollection services)
    {
        services.TryAddSingleton<IClientDirectory>(provider => new ClientDirectory(
            provider.GetRequiredService<IOptions<CloudLoginTokenOptions>>(),
            provider.GetService<IApplicationRepository>(),
            provider.GetService<ISecretKeyRepository>(),
            provider.GetService<IAuditLogger>(),
            provider.GetService<ILogger<ClientDirectory>>()));

        services.TryAddSingleton<IClientRequestAuthenticator, ClientRequestAuthenticator>();
        return services;
    }

    public static IServiceCollection AddCloudLoginBackChannelLogout(this IServiceCollection services)
    {
        services.AddCloudLoginClientDirectory();
        services.AddHttpClient(BackChannelLogoutDefaults.HttpClientName);
        services.TryAddSingleton<BackChannelLogoutService>();
        services.TryAddSingleton<ILogoutNotifier>(provider => provider.GetRequiredService<BackChannelLogoutService>());
        services.AddHostedService<BackChannelLogoutWorker>();
        return services;
    }
}
