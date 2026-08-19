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
    Task<List<UserModel>> GetTestUsers();
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

    /// <summary>
    /// Uploads a custom profile picture for the user to blob storage and updates the user record.
    /// </summary>
    /// <param name="userId">The user to update.</param>
    /// <param name="content">The raw image bytes.</param>
    /// <param name="contentType">The image content type (e.g., "image/png").</param>
    /// <returns>The public URL of the uploaded profile picture.</returns>
    Task<string> UploadProfilePicture(Guid userId, byte[] content, string contentType);

    string GetPhoneNumber(string input);

    // Admin management methods (require caller to be a Global Admin)
    Task SetUserLocked(Guid userId, bool locked);
    Task AdminResetPassword(Guid userId, string newPassword);
    Task SetGlobalAdmin(Guid userId, bool isAdmin);
    Task<int> GetUserCount();

    // Authentication methods using models
    Task<bool> PasswordLogin(PasswordLoginRequest request);
    Task<bool> TestLogin(Guid userId, bool keepMeSignedIn = false);
    Task<string> CompleteLoginRedirect(string? referer = null, bool isMobileApp = false);
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

    // Account-registry surface for the signed-in user (organizations, subscriptions, billing references).
    // Returns empty results when the account registry isn't configured on the host.
    Task<List<CloudLoginOrganization>> GetMyOrganizations();
    Task<List<AccountSubscription>> GetMySubscriptions();
    Task<AccountBillingProfile?> GetMyBillingProfile();

    /// <summary>Creates a new organization owned by the signed-in user.</summary>
    Task<CloudLoginOrganization> CreateOrganization(string name);

    /// <summary>Invites a recipient (email or phone) to an organization the signed-in user owns/administers.</summary>
    Task<CloudLoginOrganizationInvitation> InviteToOrganization(Guid organizationId, string recipient, IReadOnlyList<string>? roles = null);

    /// <summary>Updates an organization's profile fields (Name, BillingEmail, BillingContactName). Caller must be the owner/admin.</summary>
    Task<CloudLoginOrganization> UpdateOrganization(CloudLoginOrganization organization);

    /// <summary>Adds or updates a saved payment-method reference for the signed-in user (or an organization they belong to).</summary>
    Task<AccountBillingProfile> AddPaymentMethod(AccountPaymentMethodReference method, Guid? organizationId = null);

    /// <summary>Looks up an organization by id, regardless of caller membership. Used by the service-to-service lookup endpoint.</summary>
    Task<CloudLoginOrganization?> GetOrganizationById(Guid organizationId);

    /// <summary>Returns every organization in the registry, regardless of caller membership. Used by the service-to-service lookup endpoint.</summary>
    Task<List<CloudLoginOrganization>> GetAllOrganizations();

    /// <summary>Returns organization membership and string permissions for trusted service integrations.</summary>
    Task<List<CloudLoginOrganizationMember>> GetOrganizationMembers(Guid organizationId);

    /// <summary>Looks up a subscription by id, regardless of owner. Used by the service-to-service lookup endpoint.</summary>
    Task<AccountSubscription?> GetSubscriptionById(Guid subscriptionId);

    /// <summary>Returns every subscription in the registry, regardless of owner or status. Used by the service-to-service lookup endpoint.</summary>
    Task<List<AccountSubscription>> GetAllSubscriptions();
}
