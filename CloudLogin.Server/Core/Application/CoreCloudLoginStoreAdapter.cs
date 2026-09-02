using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// Implements the authority's <see cref="ICloudLoginStore"/> application contract over the
/// seven-container storage core.
/// </summary>
public sealed class CoreCloudLoginStoreAdapter(
    CoreUserService userService,
    IUserRepository users,
    ICredentialRepository credentials,
    IdentityLinkingService identityLinking,
    IdentityNormalization normalization,
    ILoginRequestRepository loginRequests,
    CloudLoginCoreConfiguration configuration,
    Microsoft.AspNetCore.Http.IHttpContextAccessor? httpContextAccessor = null,
    SessionService? sessions = null) : ICloudLoginStore
{
    private readonly CoreUserService _userService = userService;
    private readonly IUserRepository _users = users;
    private readonly ICredentialRepository _credentials = credentials;
    private readonly IdentityLinkingService _identityLinking = identityLinking;
    private readonly IdentityNormalization _normalization = normalization;
    private readonly ILoginRequestRepository _loginRequests = loginRequests;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;

    /// <summary>
    /// Creating a login request is the last step that runs in the signing-in person's browser, so
    /// it is the only place their address and user agent are observable. Optional so tests and
    /// non-web hosts can construct this without one.
    /// </summary>
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;

    // ── User reads ────────────────────────────────────────────────────────────

    public async Task<List<CloudUser>> GetUsers()
    {
        List<UserDocument> documents = await _users.GetAllAsync();
        return [.. documents.Where(user => user.State != UserStates.Deleted).Select(CoreUserService.ComposeProfileOnly)];
    }

    public Task<CloudUser?> GetUserById(Guid id) => _userService.LoadAsync(id);

    public async Task<List<CloudUser>> GetUsersByDisplayName(string displayName)
    {
        List<UserDocument> documents = await _users.GetByDisplayNameAsync(displayName);
        return [.. documents.Select(CoreUserService.ComposeProfileOnly)];
    }

    public async Task<CloudUser?> GetUserByDisplayName(string displayName) =>
        (await GetUsersByDisplayName(displayName)).FirstOrDefault();

    public async Task<CloudUser?> GetUserByInput(string input)
    {
        CloudUser? user = await GetUserByEmailAddress(input);
        return user ?? await GetUserByPhoneNumber(input);
    }

    public async Task<CloudUser?> GetUserByEmailAddress(string emailAddress)
    {
        Guid? userId = await _identityLinking.ResolveUserIdAsync(
            IdentityKey.CanonicalEmail(IdentityNormalization.NormalizeEmail(emailAddress)));

        return userId is null ? null : await _userService.LoadAsync(userId.Value);
    }

    public async Task<CloudUser?> GetUserByPhoneNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return null;

        Guid? userId = await _identityLinking.ResolveUserIdAsync(
            IdentityKey.CanonicalPhone(_normalization.NormalizePhone(number)));

        return userId is null ? null : await _userService.LoadAsync(userId.Value);
    }

    // ── Login requests (classic one-time handoff) ────────────────────────────

    public async Task<CloudUser?> GetUserByRequestId(Guid requestId)
    {
        LoginRequestDocument? request = await _loginRequests.GetAsync(requestId.ToString());

        if (request is null || request.Kind != LoginRequestKinds.Login || DocumentExpiry.IsExpired(request))
            return null;

        // Single-use: only the caller whose conditional delete lands gets the user.
        if (!await _loginRequests.TryDeleteAsync(request))
            return null;

        if (!Guid.TryParse(request.UserId, out Guid userId))
            return null;

        return await _userService.LoadAsync(userId);
    }

    public async Task<CloudRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid id = requestId ?? Guid.NewGuid();

        Microsoft.AspNetCore.Http.HttpContext? context = _httpContextAccessor?.HttpContext;

        LoginRequestDocument document = new()
        {
            Id = id.ToString(),
            Kind = LoginRequestKinds.Login,
            State = LoginRequestStates.Pending,
            UserId = userId.ToString(),
            CreatedOn = now,
            // The signing-in browser, captured here because the relying party redeems this
            // request over a back channel where only its own server is visible.
            OriginIp = context?.Connection.RemoteIpAddress?.ToString(),
            OriginUserAgent = Truncate(context?.Request.Headers.UserAgent.ToString(), 256),
            OriginSessionId = context?.User?.FindFirst(CloudLoginClaims.SessionId)?.Value,
            ExpiresOn = now + _configuration.LoginRequestLifetime
        };

        DocumentExpiry.Recompute(document, now);
        await _loginRequests.CreateAsync(document);

        CloudRequest request = new() { UserId = userId };
        request.SetId(id);
        return request;
    }

    /// <summary>
    /// The browser behind a login request, read without consuming it so the token issuance can
    /// attribute the session to the person's own device.
    /// </summary>
    public async Task<CloudLoginRequestOrigin?> GetRequestOrigin(Guid requestId)
    {
        LoginRequestDocument? request = await _loginRequests.GetAsync(requestId.ToString());

        if (request is null || DocumentExpiry.IsExpired(request))
            return null;

        if (string.IsNullOrWhiteSpace(request.OriginIp)
            && string.IsNullOrWhiteSpace(request.OriginUserAgent)
            && string.IsNullOrWhiteSpace(request.OriginSessionId))
            return null;

        return new CloudLoginRequestOrigin(request.OriginIp, request.OriginUserAgent, request.OriginSessionId);
    }

    /// <summary>A user agent is unbounded client input; cap it before it reaches storage.</summary>
    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maximumLength ? value
        : value[..maximumLength];

    // ── User writes ───────────────────────────────────────────────────────────

    public async Task Create(CloudUser user)
    {
        if (user.ID == Guid.Empty)
            user.ID = Guid.NewGuid();

        // Legacy behavior: the first registered user becomes global admin. In the core this is
        // an atomic bootstrap reservation inside the registration saga, so races cannot mint two.
        await _userService.SaveAsync(user, isCreate: true);
    }

    public async Task Update(CloudUser user)
    {
        if (user.ID == Guid.Empty)
        {
            // Legacy tolerance: resolve by any available input before refusing.
            string? candidate = user.PrimaryEmailAddress?.Input
                ?? user.EmailAddresses?.FirstOrDefault()?.Input
                ?? user.Inputs?.FirstOrDefault()?.Input;

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                CloudUser? existing = await GetUserByInput(candidate);
                if (existing is not null)
                    user.ID = existing.ID;
            }

            if (user.ID == Guid.Empty)
                throw new InvalidOperationException("Cannot update user with empty ID. Provide a valid ID or use Create.");
        }

        await _userService.SaveAsync(user, isCreate: false);
    }

    public Task DeleteUser(Guid userId) => _userService.DeleteAsync(userId);

    public async Task AddInput(Guid userId, CloudLoginInput input)
    {
        CloudUser user = await _userService.LoadAsync(userId) ?? throw new Exception("User not found.");
        user.Inputs.Add(input);
        await _userService.SaveAsync(user, isCreate: false);
    }

    public Task<int> GetUserCount() => _users.CountAsync();

    public async Task<string?> GetSecurityStamp(Guid userId)
    {
        UserDocument? user = await _users.GetAsync(userId);
        return user?.State == UserStates.Active ? user.SecurityStamp : null;
    }

    public async Task RotateSecurityStamp(Guid userId)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            UserDocument? user = await _users.GetAsync(userId);
            if (user is null)
                return;

            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.UpdatedOn = DateTimeOffset.UtcNow;

            try
            {
                await _users.ReplaceAsync(user);
                if (sessions is not null)
                    await sessions.RevokeAllForUserAsync(
                        userId, SessionRevocationReasons.SecurityStampChanged);
                return;
            }
            catch (CoreConcurrencyException) when (attempt == 0)
            {
                // Reload once so an unrelated concurrent profile update does not prevent
                // revocation of tickets issued before this security change.
            }
        }
    }

    public async Task RemoveLoginProvider(Guid userId, string providerCode, string input, string? identifier)
    {
        List<CredentialDocument> all = await _credentials.GetAllForUserAsync(userId);
        CredentialDocument? credential;

        if (string.Equals(providerCode, "Password", StringComparison.OrdinalIgnoreCase))
        {
            string normalizedValue = _normalization.NormalizeContact(
                input.Contains('@', StringComparison.Ordinal)
                    ? nameof(CloudLoginInputFormat.EmailAddress)
                    : nameof(CloudLoginInputFormat.PhoneNumber),
                input);

            // The address identifies which contact the caller means; the credential itself is
            // then found by that contact's immutable id.
            UserDocument? user = await _users.GetAsync(userId);
            Guid? contactId = user?.Contacts
                .FirstOrDefault(contact => string.Equals(contact.NormalizedValue, normalizedValue, StringComparison.Ordinal))
                ?.ContactId;

            if (contactId is null)
                return;

            credential = all.FirstOrDefault(candidate =>
                candidate.Kind == CredentialKinds.Password && candidate.ContactId == contactId);

            if (credential is null)
                return;

            await _identityLinking.EnsureNotFinalMethodAsync(userId, credential.Id);
            await _credentials.DeleteAsync(userId, credential.Id);
            await RotateSecurityStamp(userId);
            return;
        }

        credential = all.FirstOrDefault(candidate =>
            candidate.Kind == CredentialKinds.ExternalIdentity &&
            string.Equals(candidate.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(identifier) ||
             string.Equals(candidate.Subject, identifier, StringComparison.Ordinal)));

        if (credential?.Issuer is null || credential.Subject is null)
            return;

        await _identityLinking.UnlinkExternalIdentityAsync(userId, credential.Issuer, credential.Subject);
    }
}
