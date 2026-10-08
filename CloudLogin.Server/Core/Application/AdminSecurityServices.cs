using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public enum SigningKeyStatuses
{
    /// <summary>Signs new tokens and is published.</summary>
    Active,

    /// <summary>No longer signs, still published so tokens already issued verify.</summary>
    Retired,

    /// <summary>Past its publication window; kept only until the store removes it.</summary>
    Expired
}

/// <summary>Public metadata of a signing key. The private half is never part of this.</summary>
public sealed record SigningKeyView(
    string KeyId,
    DateTimeOffset CreatedOn,
    DateTimeOffset SigningExpiresOn,
    DateTimeOffset PublishExpiresOn,
    SigningKeyStatuses Status,
    bool ExpiresSoon);

public sealed record SigningKeyOverview(bool ManagedInKeyVault, IReadOnlyList<SigningKeyView> Keys);

/// <summary>Token signing keys: what is active and retired, and a deliberate rotation.</summary>
public sealed class SigningKeyAdminService(CloudLoginSigningKeyManager keys, IAuditLogger audit, TimeProvider? clock = null)
{
    public static readonly TimeSpan ExpiresSoonWindow = TimeSpan.FromDays(7);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<SigningKeyOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        IReadOnlyList<CloudLoginSigningKey> stored = await keys.ListKeysAsync(cancellationToken);

        return new SigningKeyOverview(keys.UsesKeyVault, [.. stored.Select(key => ToView(key, now))]);
    }

    /// <summary>
    /// Starts signing with a new key at once. The outgoing key keeps verifying for the publication
    /// grace window, so tokens already issued keep working.
    /// </summary>
    public async Task<SigningKeyView> RotateAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (keys.UsesKeyVault)
            throw AdminException.Invalid("Signing keys are held in Key Vault. Rotate them there, with the vault's rotation policy or a new key version.");

        CloudLoginSigningKey key = await keys.RotateAsync(cancellationToken, force: true);

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.SigningKeyRotated,
            ActorUserId = actorUserId,
            Data = new Dictionary<string, string> { ["KeyId"] = key.KeyId }
        }, cancellationToken);

        return ToView(key, _clock.GetUtcNow());
    }

    private static SigningKeyView ToView(CloudLoginSigningKey key, DateTimeOffset now)
    {
        SigningKeyStatuses status = key.CanSign(now)
            ? SigningKeyStatuses.Active
            : key.CanVerify(now) ? SigningKeyStatuses.Retired : SigningKeyStatuses.Expired;

        return new SigningKeyView(
            key.KeyId,
            key.CreatedOn,
            key.SigningExpiresOn,
            key.PublishExpiresOn,
            status,
            status == SigningKeyStatuses.Active && key.SigningExpiresOn - now <= ExpiresSoonWindow);
    }
}

public sealed record ProviderAdminView(
    string Code,
    string Label,
    bool IsExternal,
    bool VerifiesCredentials,
    int LinkedIdentities);

public sealed record ProviderLinkedUser(Guid UserId, string? DisplayName, string? ProviderEmail, DateTimeOffset LinkedOn);

/// <summary>
/// The external and internal sign-in methods this authority offers, kept apart from the applications
/// that connect to it. Providers are declared in the deployment's configuration along with their
/// credentials, so this view reports them and who has linked them, and never shows their settings.
/// </summary>
public sealed class ProviderAdminService(
    CloudLoginWebConfiguration configuration,
    ICredentialRepository credentials,
    IUserRepository users)
{
    public async Task<IReadOnlyList<ProviderAdminView>> ListAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, int> counts = (await credentials.CountExternalIdentitiesByProviderAsync(cancellationToken))
            .ToDictionary(item => item.ProviderCode, item => item.Count, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. (configuration.Providers ?? [])
                .Select(provider => provider.ToModel())
                .Select(model => new ProviderAdminView(
                    model.Code,
                    model.Label,
                    model.IsExternal,
                    model.VerifiesCredentials,
                    counts.GetValueOrDefault(model.Code)))
        ];
    }

    public async Task<IReadOnlyList<ProviderLinkedUser>> GetLinkedUsersAsync(string providerCode, int take, CancellationToken cancellationToken = default)
    {
        if (!(configuration.Providers ?? []).Any(provider => provider.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase)))
            throw AdminException.NotFound("That provider is not configured.");

        List<CredentialDocument> identities = await credentials.GetExternalIdentitiesAsync(providerCode, Math.Clamp(take, 1, 200), cancellationToken);
        List<ProviderLinkedUser> result = [];

        foreach (CredentialDocument identity in identities)
        {
            if (!Guid.TryParse(identity.UserId, out Guid userId))
                continue;

            UserDocument? user = await users.GetAsync(userId, cancellationToken);
            result.Add(new ProviderLinkedUser(
                userId,
                user is null ? null : CloudLoginDisplayName.Compose(user.FirstName, user.LastName) ?? user.Username,
                identity.ProviderEmail,
                identity.CreatedOn));
        }

        return result;
    }
}

public sealed record DashboardSummary(
    int Users,
    int ActiveSessions,
    int Applications,
    int ActiveApplications,
    int BlockedApplications,
    int DormantApplications,
    int ExpiringSecretKeys,
    int FailedAuthenticationsLast24Hours,
    IReadOnlyList<AuditEventDocument> RecentSecurityEvents);

/// <summary>The system overview. Every figure is read from stored state; nothing is cached or estimated.</summary>
public sealed class AdminDashboardService(
    IUserRepository users,
    ISessionRepository sessions,
    ApplicationService applications,
    SecretKeyService secretKeys,
    IAuditEventRepository auditEvents,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<DashboardSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        IReadOnlyList<ApplicationView> apps = await applications.ListAsync(cancellationToken);
        IReadOnlyList<SecretKeyView> expiring = await secretKeys.GetExpiringAsync(30, cancellationToken);

        int failedAuth = await auditEvents.CountAsync(new AuditEventQuery
        {
            EventTypes = [AuditEventTypes.ClientAuthenticationFailed, AuditEventTypes.LoginFailed],
            From = now.AddHours(-24)
        }, cancellationToken);

        List<AuditEventDocument> recent = await auditEvents.QueryAsync(new AuditEventQuery { Take = 10 }, cancellationToken);

        return new DashboardSummary(
            await users.CountAsync(cancellationToken),
            await sessions.CountActiveFamiliesAsync(cancellationToken),
            apps.Count,
            apps.Count(app => !app.Document.IsBlocked),
            apps.Count(app => app.Document.IsBlocked),
            apps.Count(app => applications.IsDormant(app.Document)),
            expiring.Count,
            failedAuth,
            recent);
    }
}
