using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Azure;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// Serves the <see cref="ICloudLoginTokenStore"/> contract — refresh tokens addressed by hash, revocation
/// by family, session, or user — from the core <c>Sessions</c> container, and keeps the Cosmos
/// signing-key fallback in its own <c>SigningKeys</c> container. With this in place, a
/// core-enabled deployment holds no expiring security state outside the core model.
/// </summary>
public sealed class CoreTokenStoreAdapter(
    ISessionRepository sessions,
    CosmosCoreDatabase database,
    CloudLoginCoreConfiguration configuration) : ICloudLoginTokenStore, IAtomicCloudLoginTokenStore
{
    private readonly ISessionRepository _sessions = sessions;
    private readonly CosmosCoreDatabase _database = database;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;

    // ── Signing keys (the explicit Cosmos fallback container) ────────────────

    public async Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default)
    {
        Container container = await _database.GetContainerAsync(CloudLoginCoreContainers.SigningKeysFallback, cancellationToken);

        QueryDefinition query = new("SELECT VALUE root FROM root WHERE root.pk = 'SigningKey'");
        List<CloudLoginSigningKey> keys = [];

        using FeedIterator<CloudLoginSigningKey> iterator = container.GetItemQueryIterator<CloudLoginSigningKey>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("SigningKey") });

        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudLoginSigningKey> page = await iterator.ReadNextAsync(cancellationToken);
            keys.AddRange(page);
        }

        return keys;
    }

    public async Task SaveSigningKeyAsync(CloudLoginSigningKey key, CancellationToken cancellationToken = default)
    {
        Container container = await _database.GetContainerAsync(CloudLoginCoreContainers.SigningKeysFallback, cancellationToken);
        await container.UpsertItemAsync(key, new PartitionKey(key.PartitionKeyValue), cancellationToken: cancellationToken);
    }

    // ── Refresh tokens over the Sessions container ────────────────────────────

    public async Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        SessionTokenDocument? token = await _sessions.FindTokenByIdAsync(tokenHash, cancellationToken);
        if (token is null)
            return null;

        SessionFamilyDocument? family = await _sessions.GetFamilyAsync(token.FamilyId, cancellationToken);

        CloudLoginRefreshToken record = new()
        {
            TokenHash = tokenHash,
            UserId = Guid.TryParse(token.UserId, out Guid userId) ? userId : Guid.Empty,
            FamilyId = token.FamilyId,
            SessionId = family?.SessionId ?? string.Empty,
            Audience = family?.Audience,
            Scope = family?.Scope,
            CreatedOn = token.CreatedOn,
            ExpiresOn = token.ExpiresOn ?? DateTimeOffset.MinValue,
            ConsumedOn = token.ConsumedOn,
            IsRevoked = family?.IsRevoked ?? true,
            CreatedByIp = family?.CreatedByIp,
            UserAgent = family?.UserAgent
        };

        record.SetId(Guid.NewGuid());
        return record;
    }

    public async Task SaveRefreshTokenAsync(CloudLoginRefreshToken token, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        SessionFamilyDocument? family = await _sessions.GetFamilyAsync(token.FamilyId, cancellationToken);

        SessionTokenDocument tokenDocument = new()
        {
            Id = token.TokenHash,
            FamilyId = token.FamilyId,
            UserId = token.UserId.ToString(),
            CreatedOn = token.CreatedOn,
            ConsumedOn = token.ConsumedOn,
            ExpiresOn = token.ExpiresOn
        };
        DocumentExpiry.Recompute(tokenDocument, now);

        if (family is null)
        {
            // A sign-in through this adapter is a real sign-in on a real device, so it must be
            // described the same way SessionService.IssueFamilyAsync describes one — otherwise
            // every application sign-in would show up in the account page's device list as an
            // unnamed "Unknown device".
            DeviceDescription device = DeviceDescription.Parse(token.UserAgent);

            SessionFamilyDocument newFamily = new()
            {
                Id = token.FamilyId,
                FamilyId = token.FamilyId,
                UserId = token.UserId.ToString(),
                SessionId = token.SessionId,
                Audience = token.Audience,
                Scope = token.Scope,
                CurrentTokenId = token.TokenHash,
                CreatedOn = token.CreatedOn,
                CreatedByIp = token.CreatedByIp,
                UserAgent = token.UserAgent,
                DeviceName = device.Name,
                DeviceType = device.Type,
                DeviceBrowser = device.Browser,
                DeviceOperatingSystem = device.OperatingSystem,
                LastSeenOn = token.CreatedOn,
                LastSeenIp = token.CreatedByIp,
                IsRevoked = token.IsRevoked,
                ExpiresOn = token.CreatedOn + _configuration.SessionFamilyLifetime
            };
            DocumentExpiry.Recompute(newFamily, now);

            try
            {
                await _sessions.CreateFamilyAsync(newFamily, tokenDocument, cancellationToken);
                return;
            }
            catch (CoreConflictException)
            {
                // A parallel save created the family first; fall through and save the token alone.
            }
        }
        else if (token.ConsumedOn is null && !token.IsRevoked)
        {
            // A fresh, active token becomes the family's newest generation.
            family.CurrentTokenId = token.TokenHash;
            family.LastSeenOn = now;
            family.LastSeenIp = token.CreatedByIp ?? family.LastSeenIp;
            DocumentExpiry.Recompute(family, now);

            try
            {
                await _sessions.ReplaceFamilyAsync(family, cancellationToken);
            }
            catch (CoreConcurrencyException)
            {
                // Someone advanced the family concurrently; the token itself still persists below,
                // and the reuse rules treat non-current tokens as consumed.
            }
        }

        await _sessions.UpsertTokenAsync(tokenDocument, cancellationToken);
    }

    public async Task<CloudLoginRefreshRotationResult> RotateRefreshTokenAsync(
        CloudLoginRefreshToken current,
        CloudLoginRefreshToken replacement,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SessionFamilyDocument? family = await _sessions.GetFamilyAsync(current.FamilyId, cancellationToken);
        SessionTokenDocument? token = await _sessions.GetTokenAsync(
            current.FamilyId, current.TokenHash, cancellationToken);

        if (family is null || token is null || family.IsRevoked ||
            DocumentExpiry.IsExpired(family, now) || DocumentExpiry.IsExpired(token, now))
            return CloudLoginRefreshRotationResult.Rejected;

        if (token.ConsumedOn is not null ||
            !string.Equals(family.CurrentTokenId, token.Id, StringComparison.Ordinal))
        {
            await RevokeFamilyForReuseAsync(family, cancellationToken);
            return CloudLoginRefreshRotationResult.ReuseDetected;
        }

        token.ConsumedOn = now;
        token.ReplacedByTokenId = replacement.TokenHash;
        DocumentExpiry.Recompute(token, now);

        SessionTokenDocument successor = new()
        {
            Id = replacement.TokenHash,
            FamilyId = family.FamilyId,
            UserId = family.UserId,
            CreatedOn = replacement.CreatedOn,
            ExpiresOn = family.ExpiresOn is DateTimeOffset familyExpiry &&
                familyExpiry < replacement.ExpiresOn ? familyExpiry : replacement.ExpiresOn
        };
        DocumentExpiry.Recompute(successor, now);

        family.CurrentTokenId = successor.Id;
        DocumentExpiry.Recompute(family, now);

        try
        {
            await _sessions.RotateAsync(family, token, successor, cancellationToken);
            return CloudLoginRefreshRotationResult.Succeeded;
        }
        catch (CoreConcurrencyException)
        {
            SessionFamilyDocument? currentFamily =
                await _sessions.GetFamilyAsync(current.FamilyId, cancellationToken);
            if (currentFamily is not null)
                await RevokeFamilyForReuseAsync(currentFamily, cancellationToken);

            return CloudLoginRefreshRotationResult.ReuseDetected;
        }
    }

    public async Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default)
    {
        SessionFamilyDocument? family = await _sessions.GetFamilyAsync(familyId, cancellationToken);
        if (family is null || family.IsRevoked)
            return;

        await RevokeAsync(family, cancellationToken);
    }

    public async Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        List<SessionFamilyDocument> families = await _sessions.FindFamiliesBySessionIdAsync(sessionId, cancellationToken);

        foreach (SessionFamilyDocument family in families.Where(candidate => !candidate.IsRevoked))
            await RevokeAsync(family, cancellationToken);
    }

    public async Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<SessionFamilyDocument> families = await _sessions.GetFamiliesForUserAsync(userId, cancellationToken);

        foreach (SessionFamilyDocument family in families.Where(candidate => !candidate.IsRevoked))
            await RevokeAsync(family, cancellationToken);
    }

    private async Task RevokeAsync(SessionFamilyDocument family, CancellationToken cancellationToken)
    {
        family.IsRevoked = true;
        family.RevocationReason = SessionRevocationReasons.AdminRevoked;
        family.RevokedOn = DateTimeOffset.UtcNow;
        DocumentExpiry.Recompute(family);

        try
        {
            await _sessions.ReplaceFamilyAsync(family, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            SessionFamilyDocument? current = await _sessions.GetFamilyAsync(family.FamilyId, cancellationToken);
            if (current is null || current.IsRevoked)
                return;

            current.IsRevoked = true;
            current.RevocationReason = SessionRevocationReasons.AdminRevoked;
            current.RevokedOn = DateTimeOffset.UtcNow;
            DocumentExpiry.Recompute(current);
            await _sessions.ReplaceFamilyAsync(current, cancellationToken);
        }
    }

    private async Task RevokeFamilyForReuseAsync(
        SessionFamilyDocument family, CancellationToken cancellationToken)
    {
        family.IsRevoked = true;
        family.RevocationReason = SessionRevocationReasons.TokenReuseDetected;
        family.RevokedOn = DateTimeOffset.UtcNow;
        DocumentExpiry.Recompute(family);

        try
        {
            await _sessions.ReplaceFamilyAsync(family, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            SessionFamilyDocument? latest =
                await _sessions.GetFamilyAsync(family.FamilyId, cancellationToken);
            if (latest is null || latest.IsRevoked)
                return;

            latest.IsRevoked = true;
            latest.RevocationReason = SessionRevocationReasons.TokenReuseDetected;
            latest.RevokedOn = DateTimeOffset.UtcNow;
            DocumentExpiry.Recompute(latest);
            await _sessions.ReplaceFamilyAsync(latest, cancellationToken);
        }
    }
}
