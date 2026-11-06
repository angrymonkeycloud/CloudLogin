using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>What one administrator may do, resolved from the stored user on every request.</summary>
public sealed record AdminAccess(Guid UserId, bool IsGlobalAdmin, IReadOnlyList<AdminRoles> Roles, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => Permissions.Contains(permission);

    public bool IsAdministrator => Permissions.Count > 0;
}

public interface IAdminAuthorizer
{
    Task<AdminAccess> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns the access when it includes the permission, else throws <see cref="AdminErrorKinds.Forbidden"/>.</summary>
    Task<AdminAccess> RequireAsync(Guid userId, string permission, CancellationToken cancellationToken = default);
}

/// <summary>
/// Administrative authorization is read from the user's record each time, never from a token claim:
/// a role removed here stops working immediately rather than when a token expires.
/// </summary>
public sealed class AdminAuthorizer(IUserRepository users) : IAdminAuthorizer
{
    public async Task<AdminAccess> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        UserDocument? user = userId == Guid.Empty ? null : await users.GetAsync(userId, cancellationToken);

        // A disabled, locked or deleted account holds no administrative power, whatever it was granted.
        if (user is null || user.IsLocked || user.State != UserStates.Active)
            return new AdminAccess(userId, false, [], new HashSet<string>());

        return new AdminAccess(
            userId,
            user.IsGlobalAdmin,
            [.. user.AdminRoles.Distinct()],
            AdminRolePolicy.Resolve(user.IsGlobalAdmin, user.AdminRoles));
    }

    public async Task<AdminAccess> RequireAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        AdminAccess access = await GetAccessAsync(userId, cancellationToken);

        return access.Has(permission)
            ? access
            : throw AdminException.Forbidden("Your administrator role does not allow this.");
    }
}

public sealed record AdministratorView(
    Guid UserId,
    string DisplayName,
    string? PrimaryContact,
    bool IsGlobalAdmin,
    IReadOnlyList<AdminRoles> Roles,
    bool IsLocked,
    UserStates State);

/// <summary>Lists administrators and changes the roles they hold.</summary>
public sealed class AdministratorService(IUserRepository users, IAdminAuthorizer authorizer, IAuditLogger audit)
{
    public async Task<IReadOnlyList<AdministratorView>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<UserDocument> administrators = await users.GetAdministratorsAsync(cancellationToken);

        return
        [
            .. administrators
                .OrderByDescending(user => user.IsGlobalAdmin)
                .ThenBy(user => CloudLoginDisplayName.Compose(user.FirstName, user.LastName), StringComparer.OrdinalIgnoreCase)
                .Select(ToView)
        ];
    }

    /// <summary>
    /// Replaces the access of a user; an empty list removes it. Only an active account can be granted
    /// access, but anyone can lose it. Dropping full access also clears <c>IsGlobalAdmin</c>, and the last
    /// full administrator able to act can never lose it, so the realm cannot lock itself out.
    /// </summary>
    public async Task<AdministratorView> SetRolesAsync(
        Guid actorUserId, Guid targetUserId, IReadOnlyCollection<AdminRoles> roles, CancellationToken cancellationToken = default)
    {
        await authorizer.RequireAsync(actorUserId, AdminPermissions.AdministratorsManage, cancellationToken);

        UserDocument target = await users.GetAsync(targetUserId, cancellationToken)
            ?? throw AdminException.NotFound("That account does not exist.");

        if (target.State == UserStates.Deleted)
            throw AdminException.NotFound("That account does not exist.");

        List<AdminRoles> requested = [.. roles.Distinct()];
        List<AdminRoles> before = [.. target.AdminRoles];
        bool keepsFullAccess = requested.Contains(AdminRoles.FullAdministrator);
        bool wasGlobalAdmin = target.IsGlobalAdmin;

        if (requested.Count > 0 && target.State != UserStates.Active)
            throw AdminException.Invalid("Only an active account can hold an administrator role.");

        if (CanActAsFullAdministrator(target) && !keepsFullAccess && await CountFullAdministratorsAsync(cancellationToken) <= 1)
            throw AdminException.Conflict("This is the last full administrator. Make someone else a full administrator first.");

        target.AdminRoles = requested;
        target.IsGlobalAdmin = wasGlobalAdmin && keepsFullAccess;
        target.UpdatedOn = DateTimeOffset.UtcNow;

        try
        {
            await users.ReplaceAsync(target, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            throw AdminException.Conflict("The account changed while you were editing it. Reload and try again.");
        }

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.AdminRolesChanged,
            UserId = targetUserId,
            ActorUserId = actorUserId,
            Data = new Dictionary<string, string>
            {
                ["Before"] = string.Join(',', before),
                ["After"] = string.Join(',', requested),
                ["GlobalAdmin"] = target.IsGlobalAdmin ? "true" : wasGlobalAdmin ? "removed" : "false"
            }
        }, cancellationToken);

        return ToView(target);
    }

    private static bool HoldsFullAccess(UserDocument user) => user.IsGlobalAdmin || user.AdminRoles.Contains(AdminRoles.FullAdministrator);

    private static bool CanActAsFullAdministrator(UserDocument user) => HoldsFullAccess(user) && !user.IsLocked && user.State == UserStates.Active;

    private async Task<int> CountFullAdministratorsAsync(CancellationToken cancellationToken) =>
        (await users.GetAdministratorsAsync(cancellationToken)).Count(CanActAsFullAdministrator);

    private static AdministratorView ToView(UserDocument user) => new(
        Guid.TryParse(user.Id, out Guid id) ? id : Guid.Empty,
        CloudLoginDisplayName.Compose(user.FirstName, user.LastName) ?? user.Username ?? user.Id,
        user.Contacts.OrderByDescending(contact => contact.IsPrimary).Select(contact => contact.Value).FirstOrDefault(),
        user.IsGlobalAdmin,
        [.. user.AdminRoles],
        user.IsLocked,
        user.State);
}
