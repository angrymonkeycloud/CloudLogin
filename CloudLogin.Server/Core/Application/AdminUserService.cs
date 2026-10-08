using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public sealed record AdminUserSummary(
    Guid UserId,
    string DisplayName,
    string? PrimaryContact,
    UserStates State,
    bool IsLocked,
    bool IsGlobalAdmin,
    IReadOnlyList<AdminRoles> Roles,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastSignedInOn);

public sealed record AdminContactView(string Format, string Value, bool IsPrimary, bool IsVerified, IReadOnlyList<string> ProviderCodes);

public sealed record LinkedIdentityView(string CredentialId, string? ProviderCode, string? ProviderEmail, bool EmailIsVerified, DateTimeOffset LinkedOn);

public sealed record PasskeyView(string CredentialId, string? Name, bool? IsBackedUp, DateTimeOffset RegisteredOn, DateTimeOffset? LastUsedOn);

public sealed record AdminUserDetail(
    AdminUserSummary Summary,
    string? Username,
    string? Country,
    string? Locale,
    IReadOnlyList<AdminContactView> Contacts,
    bool HasPassword,
    bool HasAuthenticatorApp,
    IReadOnlyList<PasskeyView> Passkeys,
    IReadOnlyList<LinkedIdentityView> LinkedIdentities);

/// <summary>Account administration: who someone is, how they sign in, and the actions that lock an account down.</summary>
public sealed class AdminUserService(
    IUserRepository users,
    ICredentialRepository credentials,
    SessionService sessionService,
    IAuditLogger audit)
{
    public async Task<IReadOnlyList<AdminUserSummary>> SearchAsync(string? term, int skip, int take, CancellationToken cancellationToken = default) =>
        [.. (await users.SearchAsync(term, skip, take, cancellationToken)).Select(ToSummary)];

    public async Task<AdminUserDetail> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        UserDocument user = await RequireAsync(userId, cancellationToken);
        List<CredentialDocument> all = await credentials.GetAllForUserAsync(userId, cancellationToken);

        return new AdminUserDetail(
            ToSummary(user),
            user.Username,
            user.Country,
            user.Locale,
            [.. user.Contacts.OrderByDescending(contact => contact.IsPrimary).Select(contact =>
                new AdminContactView(contact.Format, contact.Value, contact.IsPrimary, contact.IsVerified, [.. contact.ProviderCodes]))],
            all.Any(credential => credential.Kind == CredentialKinds.Password),
            all.Any(credential => credential is { Kind: CredentialKinds.Totp, TotpIsConfirmed: true }),
            [.. all.Where(credential => credential.Kind == CredentialKinds.Passkey)
                .OrderByDescending(credential => credential.CreatedOn)
                .Select(credential => new PasskeyView(credential.Id, credential.PasskeyName, credential.PasskeyIsBackedUp, credential.CreatedOn, credential.PasskeyLastUsedOn))],
            [.. all.Where(credential => credential.Kind == CredentialKinds.ExternalIdentity)
                .OrderBy(credential => credential.ProviderCode, StringComparer.OrdinalIgnoreCase)
                .Select(credential => new LinkedIdentityView(credential.Id, credential.ProviderCode, credential.ProviderEmail, credential.ProviderEmailIsVerified, credential.CreatedOn))]);
    }

    /// <summary>
    /// Disables or re-enables an account. Disabling ends every session at once and rotates the
    /// security stamp, so nothing already issued keeps working; re-enabling restores the ability to
    /// sign in, not the sessions that were ended.
    /// </summary>
    public async Task<AdminUserSummary> SetDisabledAsync(Guid actorUserId, Guid userId, bool disabled, CancellationToken cancellationToken = default)
    {
        if (disabled && actorUserId == userId)
            throw AdminException.Invalid("You cannot disable your own account.");

        UserDocument user = await RequireAsync(userId, cancellationToken);

        if (user.State is UserStates.Deleted or UserStates.PendingDeletion)
            throw AdminException.Invalid("This account is being deleted and cannot be enabled or disabled.");

        if (disabled && (user.IsGlobalAdmin || user.AdminRoles.Contains(AdminRoles.FullAdministrator)) && await CountFullAdministratorsAsync(cancellationToken) <= 1)
            throw AdminException.Conflict("This is the last full administrator and cannot be disabled.");

        UserStates target = disabled ? UserStates.Disabled : UserStates.Active;

        if (user.State != target)
        {
            user.State = target;
            user.UpdatedOn = DateTimeOffset.UtcNow;

            if (disabled)
                user.SecurityStamp = Guid.NewGuid().ToString("N");

            try
            {
                await users.ReplaceAsync(user, cancellationToken);
            }
            catch (CoreConcurrencyException)
            {
                throw AdminException.Conflict("The account changed while you were editing it. Reload and try again.");
            }

            if (disabled)
                await sessionService.RevokeAllForUserAsync(userId, SessionRevocationReasons.AdminRevoked, cancellationToken);

            await audit.LogAsync(new AuditEntry
            {
                EventType = disabled ? AuditEventTypes.UserDisabled : AuditEventTypes.UserEnabled,
                UserId = userId,
                ActorUserId = actorUserId
            }, cancellationToken);
        }

        return ToSummary(user);
    }

    /// <summary>
    /// Removes a registered passkey (a lost or stolen device) and signs the account out
    /// everywhere, since whoever held that device may still hold a session.
    /// </summary>
    public async Task RevokePasskeyAsync(Guid actorUserId, Guid userId, string credentialId, CancellationToken cancellationToken = default)
    {
        UserDocument user = await RequireAsync(userId, cancellationToken);
        CredentialDocument? passkey = await credentials.GetAsync(userId, credentialId, cancellationToken);

        if (passkey is null || passkey.Kind != CredentialKinds.Passkey)
            throw AdminException.NotFound("That passkey does not exist.");

        await credentials.DeleteAsync(userId, credentialId, cancellationToken);

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedOn = DateTimeOffset.UtcNow;

        try
        {
            await users.ReplaceAsync(user, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            throw AdminException.Conflict("The account changed while the passkey was being removed. Check it and try again.");
        }

        await sessionService.RevokeAllForUserAsync(userId, SessionRevocationReasons.AdminRevoked, cancellationToken);

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.DeviceRevoked,
            UserId = userId,
            ActorUserId = actorUserId,
            Data = new Dictionary<string, string> { ["Kind"] = "Passkey", ["Name"] = passkey.PasskeyName ?? string.Empty }
        }, cancellationToken);
    }

    private async Task<UserDocument> RequireAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.GetAsync(userId, cancellationToken) ?? throw AdminException.NotFound("That account does not exist.");

    private async Task<int> CountFullAdministratorsAsync(CancellationToken cancellationToken) =>
        (await users.GetAdministratorsAsync(cancellationToken))
            .Count(user => (user.IsGlobalAdmin || user.AdminRoles.Contains(AdminRoles.FullAdministrator)) && !user.IsLocked && user.State == UserStates.Active);

    private static AdminUserSummary ToSummary(UserDocument user) => new(
        Guid.TryParse(user.Id, out Guid id) ? id : Guid.Empty,
        CloudLoginDisplayName.Compose(user.FirstName, user.LastName) ?? user.Username ?? user.Id,
        user.Contacts.OrderByDescending(contact => contact.IsPrimary).Select(contact => contact.Value).FirstOrDefault(),
        user.State,
        user.IsLocked || user.State == UserStates.Disabled,
        user.IsGlobalAdmin,
        [.. user.AdminRoles],
        user.CreatedOn,
        user.LastSignedInOn);
}
