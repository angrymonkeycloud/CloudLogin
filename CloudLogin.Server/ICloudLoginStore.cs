namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Persistence operations required by <see cref="CloudLoginServer"/>.
/// Cosmos is the production implementation; tests can provide an in-memory store.
/// </summary>
public interface ICloudLoginStore
{
    Task<List<UserModel>> GetUsers();
    Task<UserModel?> GetUserById(Guid id);
    Task<List<UserModel>> GetUsersByDisplayName(string displayName);
    Task<UserModel?> GetUserByDisplayName(string displayName);
    Task<UserModel?> GetUserByInput(string input);
    Task<UserModel?> GetUserByEmailAddress(string emailAddress);
    Task<UserModel?> GetUserByPhoneNumber(string number);
    Task<UserModel?> GetUserByRequestId(Guid requestId);
    Task<LoginRequest> CreateRequest(Guid userId, Guid? requestId = null);
    Task Update(UserModel user);
    Task Create(UserModel user);
    Task DeleteUser(Guid userId);
    Task AddInput(Guid userId, LoginInput input);
    Task<int> GetUserCount();
}
