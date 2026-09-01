namespace AngryMonkey.CloudLogin.Server.Versioning;

/// <summary>
/// The storage schema a deployment runs on. Independent of <see cref="CloudLoginApiVersion"/>:
/// the API version chooses what a caller sees, this chooses what is written to disk, and every
/// API version runs on whichever database version is selected.
/// </summary>
/// <remarks>
/// There is no V1: V1 is an API contract only. Storage numbering starts at the schema CloudLogin
/// shipped with (<see cref="V2"/>).
/// </remarks>
public enum CloudLoginDatabaseVersion
{
    /// <summary>
    /// The existing storage: one mixed Cosmos container holding every document type, keyed by a
    /// <c>pk</c>/<c>$type</c> discriminator, with the legacy compatibility knobs on
    /// <see cref="CosmosConfiguration"/> (<c>IncludeLegacySchema</c>, <c>SaveIdMode</c>,
    /// <c>UserInfoPartitionKeyValue</c>, <c>JsonCompatibilityMode</c>) available.
    /// <para>
    /// Choose this only to keep reading a database written by an earlier CloudLogin. It requires
    /// <c>Cosmos:DatabaseId</c> and <c>Cosmos:ContainerId</c> to name that existing database.
    /// </para>
    /// </summary>
    V2 = 2,

    /// <summary>
    /// The modern seven-container model (see <c>docs/architecture-core.md</c>): separate
    /// containers for users, credentials, workspaces, access, sessions, login requests and audit
    /// events, with native TTL and the Table Storage identity index. No legacy compatibility, and
    /// none of the legacy schema knobs apply.
    /// <para>
    /// The default. A deployment that configures nothing gets this, and CloudLogin creates the
    /// database and its containers itself on startup.
    /// </para>
    /// </summary>
    V3 = 3
}
