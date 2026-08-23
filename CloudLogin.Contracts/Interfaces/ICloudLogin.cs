using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin.Interfaces;

public interface ICloudLogin
{
    string LoginUrl { get; }
    string? RedirectUri { get; set; } // Legacy property - will be deprecated
    List<CloudLoginLink>? FooterLinks { get; set; }
    CloudLoginInputFormat GetInputFormat(string input);
    Task<bool> AutomaticLogin();
    Task<List<CloudUser>> GetAllUsers();
    Task<List<CloudUser>> GetTestUsers();
    Task<CloudUser?> GetUserById(Guid userId);
    Task<List<CloudUser>> GetUsersByDisplayName(string displayName);
    Task<CloudUser?> GetUserByDisplayName(string displayName);
    Task<CloudUser?> GetUserByInput(string input);
    Task<CloudUser?> GetUserByEmailAddress(string email);
    Task<CloudUser?> GetUserByPhoneNumber(string number);
    Task<CloudUser?> GetUserByRequestId(Guid requestId);
    Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null);
    Task SendWhatsAppCode(string receiver, string code);
    Task SendEmailCode(string receiver, string code);
    Task UpdateUser(CloudUser user);
    Task CreateUser(CloudUser user);
    Task DeleteUser(Guid userId);
    Task<CloudUser?> CurrentUser();
    Task<bool> IsAuthenticated();
    Task AddUserInput(Guid userId, CloudLoginInput input);
    Task<List<CloudLoginProviderDefinition>> GetProviders();

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
    Task<bool> PasswordLogin(CloudLoginPasswordLoginRequest request);
    Task<bool> TestLogin(Guid userId, bool keepMeSignedIn = false);
    Task<string> CompleteLoginRedirect(string? referer = null, bool isMobileApp = false);
    Task<CloudUser> PasswordRegistration(CloudLoginPasswordRegistrationRequest request);
    Task<CloudUser> CodeRegistration(CloudLoginCodeRegistrationRequest request);

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

    // Account-registry surface for the signed-in user (workspaces, subscriptions, billing references).
    // Returns empty results when the account registry isn't configured on the host.
    Task<List<CloudWorkspace>> GetMyWorkspaces();

    /// <summary>
    /// The signed-in user's subscriptions. By default only the running ones; pass
    /// <paramref name="includeInactive"/> to include expired, cancelled, and suspended entries.
    /// </summary>
    Task<List<CloudSubscription>> GetMySubscriptions(bool includeInactive = false);

    Task<CloudBillingProfile?> GetMyBillingProfile();

    /// <summary>How many workspaces the signed-in user owns and belongs to, against the host's configured caps.</summary>
    Task<CloudWorkspaceQuota> GetMyWorkspaceQuota();

    /// <summary>Creates a new workspace owned by the signed-in user. Throws <see cref="CloudWorkspaceLimitReachedException"/> once the user's allowance is used up.</summary>
    Task<CloudWorkspace> CreateWorkspace(string name);

    /// <summary>Invites a recipient (email or phone) to a workspace the signed-in user owns/administers.</summary>
    Task<CloudWorkspaceInvitation> InviteToWorkspace(Guid workspaceId, string recipient, IReadOnlyList<string>? roles = null);

    /// <summary>Updates a workspace's profile and billing information. Caller must be the owner/admin.</summary>
    Task<CloudWorkspace> UpdateWorkspace(CloudWorkspace workspace);

    /// <summary>
    /// The signed-in user's view of one workspace — profile, members, subscriptions, and
    /// billing — in a single call. Null when the user isn't a member of it.
    /// </summary>
    Task<CloudWorkspaceDetail?> GetWorkspaceDetail(Guid workspaceId);

    /// <summary>
    /// Deletes a workspace the signed-in user owns, along with its memberships, invitations,
    /// billing profile, and removable subscriptions. Throws
    /// <see cref="CloudWorkspaceDeletionBlockedException"/> while a subscription still blocks it.
    /// </summary>
    Task DeleteWorkspace(Guid workspaceId);

    /// <summary>
    /// Removes a subscription entry the signed-in user (or a workspace they administer) owns,
    /// honouring its <see cref="CloudSubscription.DeletionPolicy"/>.
    /// </summary>
    Task DeleteSubscription(Guid subscriptionId);

    /// <summary>Adds or updates a saved payment-method reference for the signed-in user (or a workspace they administer).</summary>
    Task<CloudBillingProfile> AddPaymentMethod(CloudPaymentMethodReference method, Guid? workspaceId = null);

    /// <summary>Removes a saved payment-method reference from the signed-in user's account (or a workspace they administer).</summary>
    Task<CloudBillingProfile> RemovePaymentMethod(string provider, string reference, Guid? workspaceId = null);

    /// <summary>Looks up a workspace by id, regardless of caller membership. Used by the service-to-service lookup endpoint.</summary>
    Task<CloudWorkspace?> GetWorkspaceById(Guid workspaceId);

    /// <summary>Returns every workspace in the registry, regardless of caller membership. Used by the service-to-service lookup endpoint.</summary>
    Task<List<CloudWorkspace>> GetAllWorkspaces();

    /// <summary>Returns workspace membership and string permissions for trusted service integrations.</summary>
    Task<List<CloudWorkspaceMember>> GetWorkspaceMembers(Guid workspaceId);

    /// <summary>Looks up a subscription by id, regardless of owner. Used by the service-to-service lookup endpoint.</summary>
    Task<CloudSubscription?> GetSubscriptionById(Guid subscriptionId);

    /// <summary>Returns every subscription in the registry, regardless of owner or status. Used by the service-to-service lookup endpoint.</summary>
    Task<List<CloudSubscription>> GetAllSubscriptions();

    // ── Security (self-service, always scoped to the signed-in user) ──────────
    // No method here takes a user id: the server resolves the acting user from the
    // authenticated session, so this surface can't be used to reach another account.

    /// <summary>Display-safe summary of the signed-in user's security state. Contains no secrets.</summary>
    Task<CloudLoginSecurityOverview> GetSecurityOverview();

    /// <summary>The signed-in user's sign-in history, newest first.</summary>
    Task<List<CloudLoginHistoryEntry>> GetMyLoginHistory();

    /// <summary>Sets or changes the signed-in user's password.</summary>
    Task ChangeMyPassword(CloudLoginChangePasswordRequest request);

    /// <summary>Unlinks a provider from the signed-in user's account.</summary>
    Task DisconnectProvider(string providerCode, string input);

    /// <summary>Begins authenticator-app enrollment, returning the secret and its otpauth URI.</summary>
    Task<CloudLoginAuthenticatorEnrollment> BeginAuthenticatorEnrollment();

    /// <summary>Confirms enrollment with a code produced by the authenticator app.</summary>
    Task<bool> ConfirmAuthenticatorEnrollment(string code);

    /// <summary>Removes the authenticator-app enrollment.</summary>
    Task DisableAuthenticator();

    /// <summary>WebAuthn creation options, as JSON for <c>navigator.credentials.create()</c>.</summary>
    Task<string> BeginPasskeyRegistration();

    /// <summary>Verifies an attestation response and stores the resulting passkey.</summary>
    Task<CloudLoginPasskeySummary> CompletePasskeyRegistration(string optionsJson, string attestationJson, string? name);

    /// <summary>Removes a registered passkey.</summary>
    Task RemovePasskey(string credentialId);

    /// <summary>Renames a registered passkey.</summary>
    Task RenamePasskey(string credentialId, string name);
}
