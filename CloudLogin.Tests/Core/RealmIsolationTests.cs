using AngryMonkey.CloudLogin.Server.Core;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// Two realms must never share a physical name. The identity index and the Cosmos database are
/// both derived from one realm identity, so if that derivation is not injective, two authorities
/// silently share one set of users — and every cross-realm read succeeds, which is worse than
/// failing.
/// </summary>
public class RealmIsolationTests
{
    /// <summary>
    /// Realm ids that an alphanumeric-strip would flatten onto each other. This is the bug the
    /// hashed suffix exists to fix: <c>tenant-a</c> and <c>tenant_a</c> both reduce to
    /// <c>tenanta</c> once the punctuation is thrown away.
    /// </summary>
    public static TheoryData<string, string> CollidingUnderNaiveSanitizing() => new()
    {
        { "tenant-a", "tenant_a" },
        { "tenant.a", "tenant a" },
        { "a-b-c", "abc" },
        { "acme/eu", "acme-eu" },
        { "x_1", "x.1" }
    };

    [Theory]
    [MemberData(nameof(CollidingUnderNaiveSanitizing))]
    public void RealmsThatDifferOnlyInPunctuation_GetDifferentTables(string first, string second)
    {
        Assert.NotEqual(
            CloudLoginCoreContainers.IdentityKeysTableFor(first),
            CloudLoginCoreContainers.IdentityKeysTableFor(second));
    }

    [Theory]
    [MemberData(nameof(CollidingUnderNaiveSanitizing))]
    public void RealmsThatDifferOnlyInPunctuation_GetDifferentDatabases(string first, string second)
    {
        Assert.NotEqual(
            CloudLoginCoreContainers.DatabaseIdFor(first),
            CloudLoginCoreContainers.DatabaseIdFor(second));
    }

    // ── Azure naming rules ────────────────────────────────────────────────────

    [Theory]
    [InlineData("tenant-a")]
    [InlineData("acme/eu")]
    [InlineData("Ünïcödé realm")]
    [InlineData("!!!")]
    [InlineData("  spaced  ")]
    public void EveryRealm_ProducesALegalAzureTableName(string realm)
    {
        string table = CloudLoginCoreContainers.IdentityKeysTableFor(realm);

        // Azure table names: alphanumeric only, must start with a letter, 3-63 characters.
        Assert.All(table, character => Assert.True(char.IsLetterOrDigit(character), $"'{character}' is not legal in a table name."));
        Assert.True(char.IsLetter(table[0]));
        Assert.InRange(table.Length, 3, 63);
    }

    [Fact]
    public void RealmsWithNoAlphanumerics_AreStillRepresentable()
    {
        // A hash is total where a strip is not: "!!!" has nothing to keep, and used to sanitize to
        // the empty suffix — which collided with the default realm's unsuffixed table.
        string table = CloudLoginCoreContainers.IdentityKeysTableFor("!!!");

        Assert.NotEqual(CloudLoginCoreContainers.IdentityKeysTable, table);
        Assert.NotEqual(CloudLoginCoreContainers.IdentityKeysTableFor("???"), table);
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void TheSameRealm_AlwaysProducesTheSameNames()
    {
        // Not just stable within a run: these names are where the data already is, so they have to
        // be reproducible from the realm id alone, forever.
        Assert.Equal(
            CloudLoginCoreContainers.IdentityKeysTableFor("tenant-a"),
            CloudLoginCoreContainers.IdentityKeysTableFor("tenant-a"));

        Assert.Equal(
            CloudLoginCoreContainers.DatabaseIdFor("tenant-a"),
            CloudLoginCoreContainers.DatabaseIdFor("tenant-a"));
    }

    [Theory]
    [InlineData("Tenant-A", "tenant-a")]
    [InlineData("  tenant-a  ", "tenant-a")]
    public void RealmIdsAreCaseAndWhitespaceInsensitive(string first, string second)
    {
        // Matches IsDefaultRealm, which was already case-insensitive; the two would otherwise
        // disagree about whether "Default" is the default realm.
        Assert.Equal(
            CloudLoginCoreContainers.IdentityKeysTableFor(first),
            CloudLoginCoreContainers.IdentityKeysTableFor(second));
    }

    [Fact]
    public void TheSuffixIsVersioned()
    {
        // The version prefix is what lets this derivation change later without the new names
        // colliding with rows written under the old one.
        Assert.StartsWith($"v{CloudLoginCoreContainers.RealmSuffixVersion}", CloudLoginCoreContainers.RealmSuffix("tenant-a"));
    }

    // ── The default realm ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData("")]
    [InlineData(null)]
    public void TheDefaultRealm_KeepsTheUnsuffixedNames(string? realm)
    {
        Assert.True(CloudLoginCoreContainers.IsDefaultRealm(realm));
        Assert.Equal(CloudLoginCoreContainers.IdentityKeysTable, CloudLoginCoreContainers.IdentityKeysTableFor(realm!));
        Assert.Equal(CloudLoginCoreContainers.DefaultDatabaseId, CloudLoginCoreContainers.DatabaseIdFor(realm!));
    }

    [Fact]
    public void NoNamedRealm_CanReachTheDefaultRealmsNames()
    {
        // Backward compatibility for the default realm is only safe because the suffix is never
        // empty: every named realm contributes "v1" plus 16 hex characters.
        string[] realms = ["a", "!!!", "default2", "Default-", "login", "LoginIdentityKeys"];

        Assert.All(realms, realm =>
        {
            Assert.NotEqual(CloudLoginCoreContainers.IdentityKeysTable, CloudLoginCoreContainers.IdentityKeysTableFor(realm));
            Assert.NotEqual(CloudLoginCoreContainers.DefaultDatabaseId, CloudLoginCoreContainers.DatabaseIdFor(realm));
        });
    }

    [Fact]
    public void ManyRealms_DoNotCollideWithEachOther()
    {
        string[] realms = [.. Enumerable.Range(0, 500).Select(index => $"tenant-{index}")];

        Assert.Equal(realms.Length, realms.Select(CloudLoginCoreContainers.IdentityKeysTableFor).Distinct().Count());
        Assert.Equal(realms.Length, realms.Select(CloudLoginCoreContainers.DatabaseIdFor).Distinct().Count());
    }

    [Fact]
    public void TheTableAndDatabase_AgreeOnWhichRealmTheyBelongTo()
    {
        // Derived from one realm identity, so a realm's index and its users cannot end up in
        // differently-named homes.
        const string realm = "tenant-a";
        string suffix = CloudLoginCoreContainers.RealmSuffix(realm);

        Assert.EndsWith(suffix, CloudLoginCoreContainers.IdentityKeysTableFor(realm));
        Assert.EndsWith(suffix, CloudLoginCoreContainers.DatabaseIdFor(realm));
    }
}
