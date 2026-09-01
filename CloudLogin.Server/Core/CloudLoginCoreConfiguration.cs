namespace AngryMonkey.CloudLogin.Server.Core;

/// <summary>Fixed names and partition key paths of the seven core Cosmos containers.</summary>
public static class CloudLoginCoreContainers
{
    public const string Users = "Users";
    public const string UsersPartitionKey = "/id";

    public const string Credentials = "Credentials";
    public const string CredentialsPartitionKey = "/UserId";

    public const string Workspaces = "Workspaces";
    public const string WorkspacesPartitionKey = "/id";

    public const string WorkspaceAccess = "WorkspaceAccess";
    public const string WorkspaceAccessPartitionKey = "/WorkspaceId";

    public const string Sessions = "Sessions";
    public const string SessionsPartitionKey = "/FamilyId";

    public const string LoginRequests = "LoginRequests";
    public const string LoginRequestsPartitionKey = "/id";

    public const string AuditEvents = "AuditEvents";
    public const string AuditEventsPartitionKey = "/partitionKey";

    /// <summary>
    /// Azure Table Storage table names (permanent point-lookup records only).
    /// <para>
    /// Prefixed <c>Login</c> because a storage account is usually shared with the other
    /// components of the same product — an unprefixed <c>IdentityKeys</c> sitting beside another
    /// component's tables gives no clue who owns it, or whether it is safe to touch.
    /// </para>
    /// <para>
    /// No hyphen here, unlike the blob container: Azure table names permit alphanumeric
    /// characters only, so <c>Login-IdentityKeys</c> would be rejected by the service. The
    /// hyphen is used wherever the naming rules allow it (see
    /// <c>AzureStorageConfiguration.ContainerName</c>).
    /// </para>
    /// </summary>
    public const string IdentityKeysTable = "LoginIdentityKeys";
    public const string UserWorkspaceIndexTable = "LoginUserWorkspaceIndex";

    /// <summary>
    /// The identity table for one realm. The realm used to be part of every partition key; the
    /// keyed-hash layout puts the identity type and hash version there instead, so realm
    /// isolation moves up to the table itself. Two realms sharing a storage account therefore
    /// still cannot see each other's identities - which matters, because an index that silently
    /// spanned realms would resolve one realm's address to another realm's account.
    /// <para>
    /// The default realm keeps the unsuffixed name, so a deployment that never sets
    /// <see cref="CloudLoginCoreConfiguration.RealmId"/> uses exactly <c>LoginIdentityKeys</c>.
    /// A named realm can never reach that name - see <see cref="RealmSuffix"/>.
    /// </para>
    /// </summary>
    public static string IdentityKeysTableFor(string realm) =>
        IsDefaultRealm(realm) ? IdentityKeysTable : IdentityKeysTable + RealmSuffix(realm);

    /// <summary>The realm a deployment gets when it configures none.</summary>
    public const string DefaultRealmId = "default";

    public static bool IsDefaultRealm(string? realm) =>
        string.IsNullOrWhiteSpace(realm) || string.Equals(realm, DefaultRealmId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The realm-suffix construction. Bumped only if the derivation below changes.</summary>
    public const int RealmSuffixVersion = 1;

    /// <summary>
    /// The physical name fragment identifying one realm, as <c>v1</c> followed by 16 hex
    /// characters of SHA-256 over the realm id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hashed rather than sanitized because Azure table names permit alphanumeric characters only,
    /// and stripping everything else is not injective: <c>tenant-a</c> and <c>tenant_a</c> both
    /// reduce to <c>tenanta</c>, so two realms would silently share one identity index and resolve
    /// each other's addresses. A hash is total - every realm id maps somewhere, no character is
    /// unrepresentable - and injective for every input anyone will actually configure.
    /// </para>
    /// <para>
    /// Trimmed to 64 bits: this is a namespacing device, not a security boundary, and a collision
    /// needs roughly 4 billion realms in one storage account before it becomes likely. The
    /// <c>v1</c> prefix is what makes a future change to this derivation land on new names rather
    /// than quietly colliding with today's, and it also guarantees a named realm can never produce
    /// the empty suffix that would collide with the default realm's unsuffixed table.
    /// </para>
    /// <para>
    /// The realm id is normalized to lower case first, so <c>Tenant-A</c> and <c>tenant-a</c> are
    /// one realm - matching <see cref="IsDefaultRealm"/>, which is already case-insensitive.
    /// </para>
    /// </remarks>
    public static string RealmSuffix(string realm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realm);

        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(realm.Trim().ToLowerInvariant()));

        return $"v{RealmSuffixVersion}{Convert.ToHexStringLower(hash)[..16]}";
    }

    /// <summary>
    /// The optional Cosmos signing-key fallback container, used only when a deployment keeps its
    /// token signing keys in Cosmos instead of Key Vault. Partitioned by the legacy record's own
    /// <c>/pk</c> so existing key documents work unchanged; TTL retires old material.
    /// </summary>
    public const string SigningKeysFallback = "SigningKeys";
    public const string SigningKeysFallbackPartitionKey = "/pk";

    /// <summary>
    /// The default Cosmos database name for the seven-container core, shared by
    /// <see cref="CloudLoginCoreConfiguration.DatabaseId"/> (runtime) and the Aspire hosting
    /// extension's declarative provisioning (deploy time) so the two can never name the database
    /// differently and silently miss each other.
    /// </summary>
    public const string DefaultDatabaseId = "Login";

    /// <summary>
    /// The Cosmos database one realm gets when it names none: <c>Login</c> for the default realm,
    /// and <c>Login{realmSuffix}</c> for any other.
    /// </summary>
    /// <remarks>
    /// Derived from the same realm identity as <see cref="IdentityKeysTableFor"/>, so a realm's
    /// database and its identity table cannot end up disagreeing about which realm they belong to.
    /// One database per realm is the point: sharing one would put both realms' users in the same
    /// containers with no discriminator at all, and every cross-realm read would succeed - worse
    /// than failing. Configuration validation enforces the same rule for an explicitly named
    /// database.
    /// </remarks>
    public static string DatabaseIdFor(string realm) =>
        IsDefaultRealm(realm) ? DefaultDatabaseId : DefaultDatabaseId + RealmSuffix(realm);

    /// <summary>
    /// The containers that hold expiring documents and must therefore be provisioned with
    /// <c>DefaultTimeToLive = -1</c>. Cosmos ignores a document's own <c>ttl</c> entirely when
    /// container TTL is off, so a container created without it silently never expires anything.
    /// <para>
    /// One list, read by both provisioning paths - the runtime provisioner and the Aspire hosting
    /// integration's bicep - so a container can never be created TTL-armed by one and not the
    /// other.
    /// </para>
    /// </summary>
    public static bool RequiresTimeToLive(string containerName) => containerName switch
    {
        Credentials or WorkspaceAccess or Sessions or LoginRequests or AuditEvents or SigningKeysFallback => true,
        _ => false
    };
}

/// <summary>
/// Configuration of the modern CloudLogin core storage and services. Setting
/// <c>CloudLoginWebConfiguration.Core</c> activates the seven-container Cosmos model, the Table
/// Storage identity index, and the compatibility adapters that route every API version through
/// them. Leaving it unset keeps the legacy single-container store — the supported state until a
/// deployment has run the migration.
/// </summary>
public sealed class CloudLoginCoreConfiguration
{
    /// <summary>
    /// The deployment realm, isolating identity keys and audit partitions when several
    /// authorities share storage accounts. Independent of API versions and SchemaVersion.
    /// </summary>
    public string RealmId { get; set; } = CloudLoginCoreContainers.DefaultRealmId;

    private string? _databaseId;

    /// <summary>
    /// Cosmos database holding the seven core containers. Reuses the <c>Cosmos</c> account
    /// settings.
    /// <para>
    /// Unset, it resolves from <see cref="RealmId"/> - <c>Login</c> for the default realm and
    /// <c>Login{realmSuffix}</c> for any other - so one database per realm is what a deployment
    /// gets without having to arrange it. Setting it explicitly overrides that, and configuration
    /// validation then checks the name still belongs to this realm rather than another's.
    /// </para>
    /// </summary>
    public string DatabaseId
    {
        get => _databaseId ?? CloudLoginCoreContainers.DatabaseIdFor(RealmId);
        set
        {
            _databaseId = value;
            DatabaseIdExplicitlyConfigured = !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>Whether <see cref="DatabaseId"/> was named by the application rather than derived from the realm.</summary>
    internal bool DatabaseIdExplicitlyConfigured { get; private set; }

    /// <summary>How long workspace invitations stay open. Enforced via Cosmos TTL.</summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Audit event retention, enforced via Cosmos TTL on the AuditEvents container.</summary>
    public TimeSpan AuditRetention { get; set; } = TimeSpan.FromDays(400);

    /// <summary>Absolute lifetime of one refresh-token family; rotation never extends it.</summary>
    public TimeSpan SessionFamilyLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Lifetime of a single refresh-token generation inside a family.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Lifetime of a classic one-time login handoff request.</summary>
    public TimeSpan LoginRequestLifetime { get; set; } = TimeSpan.FromSeconds(60);

    public DeviceAuthorizationConfiguration DeviceAuthorization { get; set; } = new();

    public IdentityLinkingConfiguration IdentityLinking { get; set; } = new();
}

/// <summary>Provider-linking policy. Secure defaults: nothing links automatically.</summary>
public sealed class IdentityLinkingConfiguration
{
    /// <summary>
    /// When true, a provider whose issuer appears in <see cref="TrustedAutoLinkIssuers"/> may
    /// attach its identity to an existing account that owns the same verified email without the
    /// authenticated linking ceremony. Off by default — the ceremony is the rule.
    /// </summary>
    public bool AllowTrustedIssuerAutoLink { get; set; }

    /// <summary>Issuers granted automatic linking when <see cref="AllowTrustedIssuerAutoLink"/> is on.</summary>
    public List<string> TrustedAutoLinkIssuers { get; set; } = [];
}

/// <summary>RFC 8628 device authorization settings (QR / TV sign-in).</summary>
public sealed class DeviceAuthorizationConfiguration
{
    /// <summary>
    /// Registered device clients. The key is the stable client id sent to the authorize
    /// endpoint; the value is the trusted description displayed during approval.
    /// </summary>
    public Dictionary<string, string> Clients { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a device authorization request stays valid.</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Minimum seconds a device must wait between polls.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// How many faster-than-interval polls are tolerated before the request is denied outright.
    /// Each violation also returns <c>slow_down</c> per RFC 8628.
    /// </summary>
    public int MaxPollViolations { get; set; } = 30;

    /// <summary>Length of the short user code (excluding the display hyphen).</summary>
    public int UserCodeLength { get; set; } = 8;

    /// <summary>The relative path of the page where a signed-in person enters the user code.</summary>
    public string VerificationPath { get; set; } = "/device";
}
