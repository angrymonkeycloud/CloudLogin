using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// Persistence for the <c>Credentials</c> container (partition key <c>/userId</c>). Credential
/// documents never leave the server: no API in any version returns them.
/// </summary>
public interface ICredentialRepository
{
    Task<CredentialDocument?> GetAsync(Guid userId, string credentialId, CancellationToken cancellationToken = default);

    Task<List<CredentialDocument>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<CredentialDocument>> GetByKindAsync(Guid userId, CredentialKinds kind, CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> when the id already exists for the user.</summary>
    Task CreateAsync(CredentialDocument credential, CancellationToken cancellationToken = default);

    /// <summary>Upsert for rotations (password change, sign-count updates). Recomputes ttl before writing.</summary>
    Task UpsertAsync(CredentialDocument credential, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, string credentialId, CancellationToken cancellationToken = default);

    /// <summary>Removes every credential for a user (account deletion).</summary>
    Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
