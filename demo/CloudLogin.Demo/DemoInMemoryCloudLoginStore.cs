using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Tokens;

namespace CloudLogin.Demo;

/// <summary>
/// Zero-dependency <see cref="ICloudLoginStore"/> so the demo authority runs without a
/// real Cosmos database. Users only live for the lifetime of the process.
/// </summary>
public sealed class DemoInMemoryCloudLoginStore : ICloudLoginStore, ICloudLoginTokenStore
{
    private readonly Dictionary<Guid, CloudUser> _users = [];
    private readonly Dictionary<Guid, Guid> _requests = [];
    private readonly Dictionary<string, CloudLoginSigningKey> _signingKeys = [];
    private readonly Dictionary<string, CloudLoginRefreshToken> _refreshTokens = [];
    private readonly Lock _gate = new();

    public Task<List<CloudUser>> GetUsers()
    {
        lock (_gate)
            return Task.FromResult(_users.Values.ToList());
    }

    public Task<CloudUser?> GetUserById(Guid id)
    {
        lock (_gate)
            return Task.FromResult(_users.GetValueOrDefault(id));
    }

    public Task<List<CloudUser>> GetUsersByDisplayName(string displayName)
    {
        lock (_gate)
            return Task.FromResult(_users.Values
                .Where(user => string.Equals(user.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                .ToList());
    }

    public async Task<CloudUser?> GetUserByDisplayName(string displayName) =>
        (await GetUsersByDisplayName(displayName)).FirstOrDefault();

    public Task<CloudUser?> GetUserByInput(string input)
    {
        lock (_gate)
            return Task.FromResult(FindByInput(input));
    }

    public Task<CloudUser?> GetUserByEmailAddress(string emailAddress)
    {
        lock (_gate)
            return Task.FromResult(FindByInput(emailAddress, CloudLoginInputFormat.EmailAddress));
    }

    public Task<CloudUser?> GetUserByPhoneNumber(string number)
    {
        lock (_gate)
            return Task.FromResult(FindByInput(number, CloudLoginInputFormat.PhoneNumber));
    }

    public Task<CloudUser?> GetUserByRequestId(Guid requestId)
    {
        lock (_gate)
        {
            if (!_requests.Remove(requestId, out Guid userId))
                return Task.FromResult<CloudUser?>(null);

            return Task.FromResult(_users.GetValueOrDefault(userId));
        }
    }

    public Task<AngryMonkey.CloudLogin.Server.CloudRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        Guid id = requestId ?? Guid.NewGuid();

        lock (_gate)
            _requests[id] = userId;

        AngryMonkey.CloudLogin.Server.CloudRequest request = new() { UserId = userId };
        request.SetId(id);
        return Task.FromResult(request);
    }

    public Task Update(CloudUser user)
    {
        lock (_gate)
            _users[user.ID] = user;

        return Task.CompletedTask;
    }

    public Task Create(CloudUser user)
    {
        lock (_gate)
            _users[user.ID] = user;

        return Task.CompletedTask;
    }

    public Task DeleteUser(Guid userId)
    {
        lock (_gate)
            _users.Remove(userId);

        return Task.CompletedTask;
    }

    public Task AddInput(Guid userId, CloudLoginInput input)
    {
        lock (_gate)
            _users[userId].Inputs.Add(input);

        return Task.CompletedTask;
    }

    public Task<int> GetUserCount()
    {
        lock (_gate)
            return Task.FromResult(_users.Count);
    }

    public Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CloudLoginSigningKey>>([.. _signingKeys.Values]);
    }

    public Task SaveSigningKeyAsync(CloudLoginSigningKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _signingKeys[key.KeyId] = key;

        return Task.CompletedTask;
    }

    public Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_refreshTokens.GetValueOrDefault(tokenHash));
    }

    public Task SaveRefreshTokenAsync(CloudLoginRefreshToken token, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _refreshTokens[token.TokenHash] = token;

        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            foreach (CloudLoginRefreshToken token in _refreshTokens.Values.Where(token => token.FamilyId == familyId))
                token.IsRevoked = true;

        return Task.CompletedTask;
    }

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            foreach (CloudLoginRefreshToken token in _refreshTokens.Values.Where(token => token.SessionId == sessionId))
                token.IsRevoked = true;

        return Task.CompletedTask;
    }

    public Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            foreach (CloudLoginRefreshToken token in _refreshTokens.Values.Where(token => token.UserId == userId))
                token.IsRevoked = true;

        return Task.CompletedTask;
    }

    private CloudUser? FindByInput(string input, CloudLoginInputFormat? format = null) =>
        _users.Values.FirstOrDefault(user => user.Inputs.Any(candidate =>
            (!format.HasValue || candidate.Format == format.Value) &&
            string.Equals(candidate.Input, input, StringComparison.OrdinalIgnoreCase)));
}
