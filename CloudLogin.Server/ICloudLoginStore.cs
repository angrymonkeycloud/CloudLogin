namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// The browser a login request was created from, so a session can be attributed to the person's
/// own device rather than to the relying party's server that redeemed the request.
/// </summary>
public sealed record CloudLoginRequestOrigin(string? IpAddress, string? UserAgent);

/// <summary>
/// Persistence operations required by <see cref="CloudLoginServer"/>.
/// Cosmos is the production implementation; tests can provide an in-memory store.
/// </summary>
public interface ICloudLoginStore
{
    Task<List<CloudUser>> GetUsers();
    Task<CloudUser?> GetUserById(Guid id);
    Task<List<CloudUser>> GetUsersByDisplayName(string displayName);
    Task<CloudUser?> GetUserByDisplayName(string displayName);
    Task<CloudUser?> GetUserByInput(string input);
    Task<CloudUser?> GetUserByEmailAddress(string emailAddress);
    Task<CloudUser?> GetUserByPhoneNumber(string number);
    Task<CloudUser?> GetUserByRequestId(Guid requestId);
    Task<CloudRequest> CreateRequest(Guid userId, Guid? requestId = null);

    /// <summary>
    /// Where the interactive sign-in behind a login request came from, read without consuming it.
    /// <para>
    /// A relying party redeems a login request over a back channel, so the redeeming call carries
    /// its server's address and HTTP-client user agent, not the person's. This lets the token
    /// issuance attribute the session to the browser that actually signed in. Stores that keep no
    /// such record answer null and the caller falls back to the redeeming request.
    /// </para>
    /// </summary>
    Task<CloudLoginRequestOrigin?> GetRequestOrigin(Guid requestId) =>
        Task.FromResult<CloudLoginRequestOrigin?>(null);
    Task Update(CloudUser user);
    Task Create(CloudUser user);
    Task DeleteUser(Guid userId);
    Task AddInput(Guid userId, CloudLoginInput input);
    Task<int> GetUserCount();

    /// <summary>
    /// Returns the revocation stamp used by authentication tickets. Legacy stores return null;
    /// the core rotates this value whenever security-sensitive account state changes.
    /// </summary>
    Task<string?> GetSecurityStamp(Guid userId) => Task.FromResult<string?>(null);

    /// <summary>Rotates the ticket revocation stamp after a security-sensitive change.</summary>
    Task RotateSecurityStamp(Guid userId) => Task.CompletedTask;

    /// <summary>
    /// Removes the persisted credential represented by a V2 provider entry. Legacy stores keep
    /// the credential inside <see cref="CloudUser"/> and therefore need no separate operation.
    /// </summary>
    Task RemoveLoginProvider(Guid userId, string providerCode, string input, string? identifier) =>
        Task.CompletedTask;
}
