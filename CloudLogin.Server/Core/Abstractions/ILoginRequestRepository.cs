using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// Persistence for the <c>LoginRequests</c> container (partition key <c>/id</c>). All state
/// transitions go through <see cref="TryReplaceAsync"/> so claiming, approving, and consuming a
/// request each have exactly one winner.
/// </summary>
public interface ILoginRequestRepository
{
    Task<LoginRequestDocument?> GetAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> when the id already exists.</summary>
    Task CreateAsync(LoginRequestDocument request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ETag-guarded replace. Returns false instead of throwing when the precondition fails, because
    /// losing a claim race is an expected outcome, not an error.
    /// </summary>
    Task<bool> TryReplaceAsync(LoginRequestDocument request, CancellationToken cancellationToken = default);

    /// <summary>ETag-guarded delete; same single-winner semantics as <see cref="TryReplaceAsync"/>.</summary>
    Task<bool> TryDeleteAsync(LoginRequestDocument request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a pending device request by the hash of its user code. Cross-partition query, used
    /// only by the authenticated approval page.
    /// </summary>
    Task<LoginRequestDocument?> FindByUserCodeHashAsync(string userCodeHash, CancellationToken cancellationToken = default);
}
