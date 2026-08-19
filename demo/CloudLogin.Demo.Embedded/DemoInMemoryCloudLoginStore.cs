using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;

namespace CloudLogin.Demo.Embedded;

/// <summary>
/// Zero-dependency <see cref="ICloudLoginStore"/> so this embedded-host demo runs without
/// a real Cosmos database. This is an independent store from demo/CloudLogin.Demo (the
/// standalone authority demo) - the two apps do not share users.
/// </summary>
public sealed class DemoInMemoryCloudLoginStore : ICloudLoginStore
{
    private readonly Dictionary<Guid, UserModel> _users = [];
    private readonly Dictionary<Guid, Guid> _requests = [];
    private readonly Lock _gate = new();

    public Task<List<UserModel>> GetUsers()
    {
        lock (_gate)
            return Task.FromResult(_users.Values.ToList());
    }

    public Task<UserModel?> GetUserById(Guid id)
    {
        lock (_gate)
            return Task.FromResult(_users.GetValueOrDefault(id));
    }

    public Task<List<UserModel>> GetUsersByDisplayName(string displayName)
    {
        lock (_gate)
            return Task.FromResult(_users.Values
                .Where(user => string.Equals(user.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                .ToList());
    }

    public async Task<UserModel?> GetUserByDisplayName(string displayName) =>
        (await GetUsersByDisplayName(displayName)).FirstOrDefault();

    public Task<UserModel?> GetUserByInput(string input)
    {
        lock (_gate)
            return Task.FromResult(FindByInput(input));
    }

    public Task<UserModel?> GetUserByEmailAddress(string emailAddress)
    {
        lock (_gate)
            return Task.FromResult(FindByInput(emailAddress, InputFormat.EmailAddress));
    }

    public Task<UserModel?> GetUserByPhoneNumber(string number)
    {
        lock (_gate)
            return Task.FromResult(FindByInput(number, InputFormat.PhoneNumber));
    }

    public Task<UserModel?> GetUserByRequestId(Guid requestId)
    {
        lock (_gate)
        {
            if (!_requests.Remove(requestId, out Guid userId))
                return Task.FromResult<UserModel?>(null);

            return Task.FromResult(_users.GetValueOrDefault(userId));
        }
    }

    public Task<LoginRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        Guid id = requestId ?? Guid.NewGuid();

        lock (_gate)
            _requests[id] = userId;

        LoginRequest request = new() { UserId = userId };
        request.SetId(id);
        return Task.FromResult(request);
    }

    public Task Update(UserModel user)
    {
        lock (_gate)
            _users[user.ID] = user;

        return Task.CompletedTask;
    }

    public Task Create(UserModel user)
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

    public Task AddInput(Guid userId, LoginInput input)
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

    private UserModel? FindByInput(string input, InputFormat? format = null) =>
        _users.Values.FirstOrDefault(user => user.Inputs.Any(candidate =>
            (!format.HasValue || candidate.Format == format.Value) &&
            string.Equals(candidate.Input, input, StringComparison.OrdinalIgnoreCase)));
}
