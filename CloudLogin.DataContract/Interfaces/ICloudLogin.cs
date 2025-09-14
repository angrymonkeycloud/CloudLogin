using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLogin
{
    string LoginUrl { get; }
    string? RedirectUri { get; set; } // Legacy property - will be deprecated
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

    // Authentication methods using models
    Task<bool> PasswordLogin(PasswordLoginRequest request);
    Task<User> PasswordRegistration(PasswordRegistrationRequest request);
    Task<User> CodeRegistration(CodeRegistrationRequest request);

    bool IsValidPassword(string password);

    // URL Generation methods for login flows
    /// <summary>
    /// Generates a login URL for web applications
    /// </summary>
    /// <param name="referer">The external website URL that referred to CloudLogin</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <returns>The complete login URL</returns>
    string GetLoginUrl(string? referer = null, bool isMobileApp = false);

    /// <summary>
    /// Generates a login URL for external provider authentication
    /// </summary>
    /// <param name="providerCode">The provider code (e.g., "google", "microsoft")</param>
    /// <param name="referer">The external website URL that referred to CloudLogin (legacy parameter name)</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <param name="keepMeSignedIn">Whether to maintain persistent session</param>
    /// <param name="finalReferer">The external website URL that referred to CloudLogin</param>
    /// <returns>The complete provider login URL</returns>
    string GetProviderLoginUrl(string providerCode, string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false);

    /// <summary>
    /// Generates a custom login URL with additional parameters
    /// </summary>
    /// <param name="referer">The external website URL that referred to CloudLogin (legacy parameter name)</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <param name="keepMeSignedIn">Whether to maintain persistent session</param>
    /// <param name="userHint">Optional user hint (email/phone)</param>
    /// <param name="finalReferer">The external website URL that referred to CloudLogin</param>
    /// <returns>The complete custom login URL</returns>
    string GetCustomLoginUrl(string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false, string? userHint = null);
}
