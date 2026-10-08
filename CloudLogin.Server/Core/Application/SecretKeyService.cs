using System.Security.Cryptography;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public enum SecretKeyStatuses
{
    Active,
    Expired,
    Revoked
}

public sealed record SecretKeyView(SecretKeyDocument Document, SecretKeyStatuses Status);

public sealed record IssuedSecretKey(SecretKeyDocument Key, string Secret);

/// <summary>
/// The secret keys websites with a backend authenticate with. Create as many as needed, with or without an expiry; any website may use any
/// of them. A key's secret is shown once and only its hash is kept. Keys the deployment declares are listed too and can be revoked.
/// </summary>
public sealed class SecretKeyService(
    ISecretKeyRepository keys,
    IClientDirectory directory,
    IAuditLogger audit,
    IOptions<CloudLoginTokenOptions> options,
    TimeProvider? clock = null)
{
    public const string SecretPrefix = "clsk_";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<SecretKeyView>> ListAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        List<SecretKeyDocument> stored = await keys.GetAllAsync(cancellationToken);

        // A deployment key is recorded the first time it is used; until then it is still listed, so it can be revoked before use.
        foreach (string hash in options.Value.SecretKeyHashes)
        {
            string id = ClientDirectory.DeploymentKeyId(hash);

            if (stored.All(key => key.Id != id))
                stored.Add(new SecretKeyDocument { Id = id, Label = "Declared by the deployment", SecretHash = hash, IsDeployment = true });
        }

        return [.. stored
            .Select(key => new SecretKeyView(key, StatusOf(key, now)))
            .OrderBy(view => view.Status)
            .ThenByDescending(view => view.Document.CreatedOn)];
    }

    public async Task<IssuedSecretKey> CreateAsync(Guid actorUserId, string? label, DateTimeOffset? expiresOn, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string name = string.IsNullOrWhiteSpace(label) ? "Secret key" : label.Trim();

        if (name.Length > 60)
            throw AdminException.Invalid("A label can be at most 60 characters.");

        if (expiresOn is { } expires && expires <= now)
            throw AdminException.Invalid("The expiry must be in the future, or left empty for a key that does not expire.");

        string secret = SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        SecretKeyDocument key = new()
        {
            Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)),
            Label = name,
            SecretHash = CloudLoginTokenOptions.HashSecret(secret),
            Prefix = secret[..(SecretPrefix.Length + 4)],
            CreatedOn = now,
            CreatedByUserId = actorUserId.ToString(),
            ExpiresOn = expiresOn
        };

        await keys.CreateAsync(key, cancellationToken);
        directory.Invalidate();

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.SecretKeyCreated,
            ActorUserId = actorUserId,
            Data = new Dictionary<string, string>
            {
                ["KeyId"] = key.Id,
                ["Label"] = key.Label,
                ["ExpiresOn"] = expiresOn?.ToString("o") ?? "never"
            }
        }, cancellationToken);

        return new IssuedSecretKey(key, secret);
    }

    /// <summary>Stops a key working at once, for every website using it.</summary>
    public async Task RevokeAsync(Guid actorUserId, string keyId, string? reason, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string? why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 200)];

        for (int attempt = 0; attempt < 3; attempt++)
        {
            SecretKeyDocument? key = await keys.GetAsync(keyId, cancellationToken);

            if (key is null)
            {
                string? hash = options.Value.SecretKeyHashes.FirstOrDefault(candidate => ClientDirectory.DeploymentKeyId(candidate) == keyId)
                    ?? throw AdminException.NotFound("That secret key does not exist.");

                try
                {
                    await keys.CreateAsync(new SecretKeyDocument
                    {
                        Id = keyId,
                        Label = "Declared by the deployment",
                        SecretHash = hash,
                        IsDeployment = true,
                        RevokedOn = now,
                        RevokedByUserId = actorUserId.ToString(),
                        RevocationReason = why
                    }, cancellationToken);
                }
                catch (CoreConflictException)
                {
                    continue;
                }
            }
            else
            {
                if (key.RevokedOn is not null)
                    throw AdminException.Conflict("That secret key is already revoked.");

                key.RevokedOn = now;
                key.RevokedByUserId = actorUserId.ToString();
                key.RevocationReason = why;

                if (!await keys.TryReplaceAsync(key, cancellationToken))
                    continue;
            }

            directory.Invalidate();

            await audit.LogAsync(new AuditEntry
            {
                EventType = AuditEventTypes.SecretKeyRevoked,
                ActorUserId = actorUserId,
                Data = new Dictionary<string, string> { ["KeyId"] = keyId, ["Reason"] = why ?? string.Empty }
            }, cancellationToken);

            return;
        }

        throw AdminException.Conflict("The secret key changed while it was being revoked. Try again.");
    }

    /// <summary>Keys that expire within <paramref name="withinDays"/> days, or already have, and are not revoked.</summary>
    public async Task<IReadOnlyList<SecretKeyView>> GetExpiringAsync(int withinDays = 30, CancellationToken cancellationToken = default)
    {
        DateTimeOffset horizon = _clock.GetUtcNow().AddDays(Math.Clamp(withinDays, 1, 365));

        return [.. (await ListAsync(cancellationToken)).Where(view => view.Document.RevokedOn is null && view.Document.ExpiresOn is { } expires && expires <= horizon)];
    }

    private static SecretKeyStatuses StatusOf(SecretKeyDocument key, DateTimeOffset now) =>
        key.RevokedOn is not null ? SecretKeyStatuses.Revoked
        : key.ExpiresOn is { } expires && expires <= now ? SecretKeyStatuses.Expired
        : SecretKeyStatuses.Active;
}
