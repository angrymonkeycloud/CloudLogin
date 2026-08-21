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

    /// <summary>
    /// The signed-in user's subscriptions. By default only the running ones; pass
    /// <paramref name="includeInactive"/> to include expired, cancelled, and suspended entries.
    /// </summary>
    Task<List<AccountSubscription>> GetMySubscriptions(bool includeInactive = false);

    Task<AccountBillingProfile?> GetMyBillingProfile();

    /// <summary>How many organizations the signed-in user owns and belongs to, against the host's configured caps.</summary>
    Task<OrganizationQuota> GetMyOrganizationQuota();

    /// <summary>Creates a new organization owned by the signed-in user. Throws <see cref="OrganizationLimitReachedException"/> once the user's allowance is used up.</summary>
    Task<CloudLoginOrganization> CreateOrganization(string name);

    /// <summary>Invites a recipient (email or phone) to an organization the signed-in user owns/administers.</summary>
    Task<CloudLoginOrganizationInvitation> InviteToOrganization(Guid organizationId, string recipient, IReadOnlyList<string>? roles = null);

    /// <summary>Updates an organization's profile and billing information. Caller must be the owner/admin.</summary>
    Task<CloudLoginOrganization> UpdateOrganization(CloudLoginOrganization organization);

    /// <summary>
    /// The signed-in user's view of one organization — profile, members, subscriptions, and
    /// billing — in a single call. Null when the user isn't a member of it.
    /// </summary>
    Task<OrganizationWorkspace?> GetOrganizationWorkspace(Guid organizationId);

    /// <summary>
    /// Deletes an organization the signed-in user owns, along with its memberships, invitations,
    /// billing profile, and removable subscriptions. Throws
    /// <see cref="OrganizationDeletionBlockedException"/> while a subscription still blocks it.
    /// </summary>
    Task DeleteOrganization(Guid organizationId);

    /// <summary>
    /// Removes a subscription entry the signed-in user (or an organization they administer) owns,
    /// honouring its <see cref="AccountSubscription.DeletionPolicy"/>.
    /// </summary>
    Task DeleteSubscription(Guid subscriptionId);

    /// <summary>Adds or updates a saved payment-method reference for the signed-in user (or an organization they administer).</summary>
    Task<AccountBillingProfile> AddPaymentMethod(AccountPaymentMethodReference method, Guid? organizationId = null);

    /// <summary>Removes a saved payment-method reference from the signed-in user's account (or an organization they administer).</summary>
    Task<AccountBillingProfile> RemovePaymentMethod(string provider, string reference, Guid? organizationId = null);

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

    // ── Security (self-service, always scoped to the signed-in user) ──────────
    // No method here takes a user id: the server resolves the acting user from the
    // authenticated session, so this surface can't be used to reach another account.

    /// <summary>Display-safe summary of the signed-in user's security state. Contains no secrets.</summary>
    Task<SecurityOverview> GetSecurityOverview();

    /// <summary>The signed-in user's sign-in history, newest first.</summary>
    Task<List<LoginHistoryEntry>> GetMyLoginHistory();

    /// <summary>Sets or changes the signed-in user's password.</summary>
    Task ChangeMyPassword(ChangePasswordRequest request);

    /// <summary>Unlinks a provider from the signed-in user's account.</summary>
    Task DisconnectProvider(string providerCode, string input);

    /// <summary>Begins authenticator-app enrollment, returning the secret and its otpauth URI.</summary>
    Task<AuthenticatorEnrollmentModel> BeginAuthenticatorEnrollment();

    /// <summary>Confirms enrollment with a code produced by the authenticator app.</summary>
    Task<bool> ConfirmAuthenticatorEnrollment(string code);

    /// <summary>Removes the authenticator-app enrollment.</summary>
    Task DisableAuthenticator();

    /// <summary>WebAuthn creation options, as JSON for <c>navigator.credentials.create()</c>.</summary>
    Task<string> BeginPasskeyRegistration();

    /// <summary>Verifies an attestation response and stores the resulting passkey.</summary>
    Task<PasskeySummary> CompletePasskeyRegistration(string optionsJson, string attestationJson, string? name);

    /// <summary>Removes a registered passkey.</summary>
    Task RemovePasskey(string credentialId);

    /// <summary>Renames a registered passkey.</summary>
    Task RenamePasskey(string credentialId, string name);
}
