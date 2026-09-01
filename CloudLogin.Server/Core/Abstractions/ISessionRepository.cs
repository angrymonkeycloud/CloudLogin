using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// Persistence for the <c>Sessions</c> container (partition key <c>/familyId</c>). Only token
/// hashes are ever stored; the raw refresh token exists solely in the caller's hands.
/// </summary>
public interface ISessionRepository
{
    Task<SessionFamilyDocument?> GetFamilyAsync(string familyId, CancellationToken cancellationToken = default);

    Task<SessionTokenDocument?> GetTokenAsync(string familyId, string tokenId, CancellationToken cancellationToken = default);

    /// <summary>Creates the family head and its first token atomically (transactional batch).</summary>
    Task CreateFamilyAsync(SessionFamilyDocument family, SessionTokenDocument firstToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates atomically in one transactional batch: marks <paramref name="consumedToken"/>
    /// consumed (ttl recomputed from its unchanged absolute expiry), creates
    /// <paramref name="newToken"/>, and advances the family head — all guarded by the ETags read
    /// beforehand. Throws <see cref="CoreConcurrencyException"/> when any leg lost a race, which
    /// callers must treat as possible token reuse.
    /// </summary>
    Task RotateAsync(SessionFamilyDocument family, SessionTokenDocument consumedToken, SessionTokenDocument newToken, CancellationToken cancellationToken = default);

    /// <summary>ETag-guarded replace of the family head (revocation).</summary>
    Task ReplaceFamilyAsync(SessionFamilyDocument family, CancellationToken cancellationToken = default);

    /// <summary>Cross-partition query: every non-revoked family of a user (session management UI).</summary>
    Task<List<SessionFamilyDocument>> GetFamiliesForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    // ── V2 token-surface support ──────────────────────────────────────────────
    // The legacy refresh contract addresses tokens by hash alone and sessions by sid, without
    // knowing the family. These lookups let the V2 compatibility adapter serve that contract
    // from the same Sessions container the modern paths use.

    /// <summary>Cross-partition lookup of a token document by its id (the token hash).</summary>
    Task<SessionTokenDocument?> FindTokenByIdAsync(string tokenId, CancellationToken cancellationToken = default);

    /// <summary>Cross-partition query for the families of one sign-in session.</summary>
    Task<List<SessionFamilyDocument>> FindFamiliesBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plain upsert of a token document, used by the V2 compatibility path whose contract
    /// consumes and creates tokens in separate calls rather than one batch.
    /// </summary>
    Task UpsertTokenAsync(SessionTokenDocument token, CancellationToken cancellationToken = default);
}
