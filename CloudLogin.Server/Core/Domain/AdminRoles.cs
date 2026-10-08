using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>
/// Administrative roles. A user holds none, one or several; <c>IsGlobalAdmin</c> on the user is
/// the full administrator and always implies <see cref="FullAdministrator"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdminRoles
{
    /// <summary>Everything, including managing other administrators.</summary>
    FullAdministrator,

    /// <summary>Accounts: view, disable and enable, revoke sessions and devices.</summary>
    UserAdministrator,

    /// <summary>The applications using CloudLogin (block, unblock, end their sessions) and the secret keys websites authenticate with.</summary>
    ApplicationAdministrator,

    /// <summary>Signing keys, secret key revocation, providers, sessions and the audit trail.</summary>
    SecurityAdministrator,

    /// <summary>Read-only access to everything, including the audit trail.</summary>
    AuditReader
}

/// <summary>The individual permissions a role grants. Strings so they travel in API responses unchanged.</summary>
public static class AdminPermissions
{
    public const string DashboardRead = "dashboard.read";

    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";

    public const string SessionsRead = "sessions.read";
    public const string SessionsRevoke = "sessions.revoke";

    public const string ApplicationsRead = "applications.read";
    public const string ApplicationsManage = "applications.manage";
    public const string SecretKeysManage = "secretkeys.manage";
    public const string SecretKeysRevoke = "secretkeys.revoke";


    public const string KeysRead = "keys.read";
    public const string KeysManage = "keys.manage";

    public const string ProvidersRead = "providers.read";
    public const string ProvidersManage = "providers.manage";

    public const string AuditRead = "audit.read";

    public const string AdministratorsRead = "administrators.read";
    public const string AdministratorsManage = "administrators.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        DashboardRead, UsersRead, UsersManage, SessionsRead, SessionsRevoke,
        ApplicationsRead, ApplicationsManage, SecretKeysManage, SecretKeysRevoke,
        KeysRead, KeysManage, ProvidersRead, ProvidersManage,
        AuditRead, AdministratorsRead, AdministratorsManage
    ];
}

/// <summary>Maps roles to permissions. One place, so no controller decides on its own what a role means.</summary>
public static class AdminRolePolicy
{
    private static readonly string[] ReadAll =
    [
        AdminPermissions.DashboardRead, AdminPermissions.UsersRead, AdminPermissions.SessionsRead,
        AdminPermissions.ApplicationsRead, AdminPermissions.KeysRead,
        AdminPermissions.ProvidersRead, AdminPermissions.AuditRead, AdminPermissions.AdministratorsRead
    ];

    public static IReadOnlyCollection<string> PermissionsFor(AdminRoles role) => role switch
    {
        AdminRoles.FullAdministrator => AdminPermissions.All,

        AdminRoles.UserAdministrator =>
        [
            AdminPermissions.DashboardRead, AdminPermissions.UsersRead, AdminPermissions.UsersManage,
            AdminPermissions.SessionsRead, AdminPermissions.SessionsRevoke
        ],

        AdminRoles.ApplicationAdministrator =>
        [
            AdminPermissions.DashboardRead, AdminPermissions.ApplicationsRead, AdminPermissions.ApplicationsManage,
            AdminPermissions.SecretKeysManage, AdminPermissions.SecretKeysRevoke,
            AdminPermissions.SessionsRead
        ],

        AdminRoles.SecurityAdministrator =>
        [
            AdminPermissions.DashboardRead, AdminPermissions.UsersRead, AdminPermissions.SessionsRead,
            AdminPermissions.SessionsRevoke, AdminPermissions.ApplicationsRead, AdminPermissions.SecretKeysRevoke,
            AdminPermissions.KeysRead, AdminPermissions.KeysManage,
            AdminPermissions.ProvidersRead, AdminPermissions.ProvidersManage, AdminPermissions.AuditRead
        ],

        AdminRoles.AuditReader => ReadAll,

        _ => []
    };

    /// <summary>The permissions a user effectively holds. A global administrator holds every one.</summary>
    public static IReadOnlySet<string> Resolve(bool isGlobalAdmin, IEnumerable<AdminRoles>? roles)
    {
        HashSet<string> permissions = new(StringComparer.Ordinal);

        if (isGlobalAdmin)
            permissions.UnionWith(PermissionsFor(AdminRoles.FullAdministrator));

        foreach (AdminRoles role in roles ?? [])
            permissions.UnionWith(PermissionsFor(role));

        return permissions;
    }
}
