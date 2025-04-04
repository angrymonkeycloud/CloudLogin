
namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLogin
{
    string LoginUrl { get; }
    string? RedirectUri { get; set; }
    List<Link>? FooterLinks { get; set; }
    InputFormat GetInputFormat(string input);
    Task<bool> AutomaticLogin();
    Task<List<User>> GetAllUsers();
    Task<User?> GetUserById(Guid userId);
    Task<List<User>> GetUsersByDisplayName(string displayName);
    Task<User?> GetUserByDisplayName(string displayName);
    Task<User?> GetUserByInput(string input);
    Task<User?> GetUserByEmailAddress(string email);
    Task<User?> GetUserByPhoneNumber(string number);
    Task<User?> GetUserByRequestId(Guid requestId);
    Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null);
    Task SendWhatsAppCode(string receiver, string code);
    Task SendEmailCode(string receiver, string code);
    Task UpdateUser(User user);
    Task CreateUser(User user);
    Task DeleteUser(Guid userId);
    Task<User?> CurrentUser();
    Task<bool> IsAuthenticated();
    Task AddUserInput(Guid userId, LoginInput input);
    Task<List<ProviderDefinition>> GetProviders();

    string GetPhoneNumber(string input);
}
