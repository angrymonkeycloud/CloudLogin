namespace AngryMonkey.CloudLogin.Server;

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
    Task Update(CloudUser user);
    Task Create(CloudUser user);
    Task DeleteUser(Guid userId);
    Task AddInput(Guid userId, CloudLoginInput input);
    Task<int> GetUserCount();
}
