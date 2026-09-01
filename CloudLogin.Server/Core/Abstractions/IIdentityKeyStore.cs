using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// One identity being claimed for a user. Carries the canonical value because the store is what
/// keys it - the plaintext never reaches storage, only the HMAC the store derives from it.
/// </summary>
public sealed record IdentityKeyClaim
{
    public required IdentityKeyTypes Type { get; init; }

    /// <summary>The canonical identity string (see <see cref="IdentityKey.CanonicalEmail"/> and friends).</summary>
    public required string CanonicalValue { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>The immutable contact this identity belongs to. Optional for external identities.</summary>
    public Guid? ContactId { get; init; }
}

/// <summary>
/// The permanent identity index in Azure Table Storage: normalized email, phone, and
/// <c>(issuer, subject)</c> external identities resolved to a user id by point lookup.
/// <para>
/// Row keys are keyed hashes (<see cref="IdentityKeyHasher"/>), so the table reveals nothing
/// about which addresses have accounts. Inserts are create-only, so two users can never silently
/// claim the same identity - the loser gets <see cref="CoreConflictException"/>. Expiring records
/// are forbidden here by design; the table also holds the one-time bootstrap reservation that
/// decides the first global administrator atomically.
/// </para>
/// </summary>
public interface IIdentityKeyStore
{
    /// <summary>Resolves a canonical identity to its owner. Null when unclaimed.</summary>
    Task<IdentityKey?> ResolveAsync(string realm, string canonicalValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create-only insert. Throws <see cref="CoreConflictException"/> when the identity is already
    /// claimed — by anyone, including the same user (callers treat same-user conflicts as
    /// idempotent success).
    /// </summary>
    Task InsertAsync(string realm, IdentityKeyClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Removes an identity claim (unlink, account deletion).</summary>
    Task DeleteAsync(string realm, string canonicalValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes only when the current record is still owned by the expected user. Conditional on
    /// the row's ETag, so a claim re-made by someone else between the read and the delete
    /// survives instead of being removed by a stale decision.
    /// </summary>
    async Task<bool> DeleteIfOwnedAsync(
        string realm, string canonicalValue, Guid expectedUserId,
        CancellationToken cancellationToken = default)
    {
        IdentityKey? current = await ResolveAsync(realm, canonicalValue, cancellationToken);
        if (current?.UserId != expectedUserId)
            return false;

        await DeleteAsync(realm, canonicalValue, cancellationToken);
        return true;
    }

    /// <summary>
    /// Atomically reserves the named one-time bootstrap slot (for example the first-administrator
    /// grant). Returns true for exactly one caller ever; everyone else gets false.
    /// </summary>
    Task<bool> TryReserveBootstrapAsync(string realm, string slotName, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Releases a bootstrap reservation only when this failed saga owns it.</summary>
    Task ReleaseBootstrapAsync(
        string realm, string slotName, Guid expectedUserId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Optional, non-authoritative user-to-workspace lookup acceleration in Azure Table Storage.
/// The authoritative answer is always the <c>WorkspaceAccess</c> container; this index is
/// maintained idempotently and repaired by reconciliation, so a missed write only costs speed.
/// </summary>
public interface IUserWorkspaceIndexStore
{
    Task<List<Guid>> GetWorkspaceIdsAsync(string realm, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Idempotent upsert — safe to replay from an outbox or reconciliation sweep.</summary>
    Task UpsertAsync(string realm, Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string realm, Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
}
