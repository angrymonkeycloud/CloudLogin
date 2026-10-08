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
    bool IsLocked);

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
    /// Replaces the roles of a user. The user must be an existing account; the last full
    /// administrator can never be removed, so the realm cannot lock itself out.
    /// </summary>
    public async Task<AdministratorView> SetRolesAsync(
        Guid actorUserId, Guid targetUserId, IReadOnlyCollection<AdminRoles> roles, CancellationToken cancellationToken = default)
    {
        await authorizer.RequireAsync(actorUserId, AdminPermissions.AdministratorsManage, cancellationToken);

        UserDocument target = await users.GetAsync(targetUserId, cancellationToken)
            ?? throw AdminException.NotFound("That account does not exist.");

        if (target.State != UserStates.Active)
            throw AdminException.Invalid("Only an active account can hold an administrator role.");

        List<AdminRoles> requested = [.. roles.Distinct()];
        List<AdminRoles> before = [.. target.AdminRoles];

        if (target.IsGlobalAdmin && !requested.Contains(AdminRoles.FullAdministrator))
            requested.Add(AdminRoles.FullAdministrator);

        bool losesFullAccess = HoldsFullAccess(target) && !requested.Contains(AdminRoles.FullAdministrator) && !target.IsGlobalAdmin;

        if (losesFullAccess && await CountFullAdministratorsAsync(cancellationToken) <= 1)
            throw AdminException.Conflict("This is the last full administrator. Make someone else a full administrator first.");

        target.AdminRoles = requested;
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
                ["After"] = string.Join(',', requested)
            }
        }, cancellationToken);

        return ToView(target);
    }

    private static bool HoldsFullAccess(UserDocument user) => user.IsGlobalAdmin || user.AdminRoles.Contains(AdminRoles.FullAdministrator);

    private async Task<int> CountFullAdministratorsAsync(CancellationToken cancellationToken) =>
        (await users.GetAdministratorsAsync(cancellationToken)).Count(user => HoldsFullAccess(user) && !user.IsLocked && user.State == UserStates.Active);

    private static AdministratorView ToView(UserDocument user) => new(
        Guid.TryParse(user.Id, out Guid id) ? id : Guid.Empty,
        CloudLoginDisplayName.Compose(user.FirstName, user.LastName) ?? user.Username ?? user.Id,
        user.Contacts.OrderByDescending(contact => contact.IsPrimary).Select(contact => contact.Value).FirstOrDefault(),
        user.IsGlobalAdmin,
        [.. user.AdminRoles],
        user.IsLocked);
}
