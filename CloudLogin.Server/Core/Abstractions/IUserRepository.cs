using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// Persistence for the <c>Users</c> container. Point operations only take the user id — the
/// partition key is <c>/id</c>. Identity resolution (email/phone/external to user id) is not
/// here: that is <see cref="IIdentityKeyStore"/>'s job.
/// </summary>
public interface IUserRepository
{
    Task<UserDocument?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> when the id already exists.</summary>
    Task CreateAsync(UserDocument user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace guarded by the document's ETag. Throws <see cref="CoreConcurrencyException"/> when
    /// the stored document changed since it was read.
    /// </summary>
    Task ReplaceAsync(UserDocument user, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);

    Task UpdateLastSignedInAsync(Guid userId, DateTimeOffset lastSignedIn, CancellationToken cancellationToken = default);

    /// <summary>Cross-partition scan; administrative use only.</summary>
    Task<List<UserDocument>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<UserDocument>> GetByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Users holding <paramref name="normalizedValue"/> as a contact point, whatever the identity
    /// index says.
    /// </summary>
    /// <remarks>
    /// Not a resolution path — <see cref="IIdentityKeyStore"/> remains the only way an identity is
    /// resolved to an account, and this deliberately cannot replace it (it reads plaintext contacts,
    /// which is exactly what the keyed index exists to avoid relying on). It is the second opinion
    /// registration consults before creating an account, so an index that has stopped answering
    /// produces a refusal rather than a duplicate person.
    /// </remarks>
    Task<List<UserDocument>> GetByNormalizedContactAsync(string normalizedValue, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
