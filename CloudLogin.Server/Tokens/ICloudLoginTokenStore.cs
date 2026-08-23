namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Persistence for the two pieces of long-lived token state: the signing keys the
/// authority mints with, and the rotating refresh tokens it hands out.
/// </summary>
public interface ICloudLoginTokenStore
{
    Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default);

    Task SaveSigningKeyAsync(CloudLoginSigningKey key, CancellationToken cancellationToken = default);

    Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task SaveRefreshTokenAsync(CloudLoginRefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every token in a rotation family. Called on reuse detection, when a
    /// consumed token is presented a second time.
    /// </summary>
    Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every refresh token issued for one sign-in session.</summary>
    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every refresh token belonging to a user, across all sessions.</summary>
    Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
