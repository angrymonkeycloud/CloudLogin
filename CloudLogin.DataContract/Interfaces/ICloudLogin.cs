using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLogin
{
    string LoginUrl { get; }
    string? RedirectUri { get; set; } // Legacy property - will be deprecated
    List<Link>? FooterLinks { get; set; }
    InputFormat GetInputFormat(string input);
    Task<bool> AutomaticLogin();
    Task<List<UserModel>> GetAllUsers();
    Task<UserModel?> GetUserById(Guid userId);
    Task<List<UserModel>> GetUsersByDisplayName(string displayName);
    Task<UserModel?> GetUserByDisplayName(string displayName);
    Task<UserModel?> GetUserByInput(string input);
    Task<UserModel?> GetUserByEmailAddress(string email);
    Task<UserModel?> GetUserByPhoneNumber(string number);
    Task<UserModel?> GetUserByRequestId(Guid requestId);
    Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null);
    Task SendWhatsAppCode(string receiver, string code);
    Task SendEmailCode(string receiver, string code);
    Task UpdateUser(UserModel user);
    Task CreateUser(UserModel user);
    Task DeleteUser(Guid userId);
    Task<UserModel?> CurrentUser();
    Task<bool> IsAuthenticated();
    Task AddUserInput(Guid userId, LoginInput input);
    Task<List<ProviderDefinition>> GetProviders();

    string GetPhoneNumber(string input);

    // Authentication methods using models
    Task<bool> PasswordLogin(PasswordLoginRequest request);
    Task<UserModel> PasswordRegistration(PasswordRegistrationRequest request);
    Task<UserModel> CodeRegistration(CodeRegistrationRequest request);

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
