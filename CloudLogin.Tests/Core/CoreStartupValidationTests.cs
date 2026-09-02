using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// What a CloudLogin deployment must get right before it is allowed to start. Every rule here exists
/// because the alternative failure is silent: an unresolvable identity index, or two realms
/// quietly sharing one set of containers.
/// </summary>
public class CoreStartupValidationTests
{
    private static CloudLoginWebConfiguration ValidConfiguration() => new()
    {
        Cosmos = new CosmosConfiguration { ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=key" },
        AzureStorage = new AzureStorageConfiguration { ConnectionString = "UseDevelopmentStorage=true" },
        IdentityHmacSecret = TestIdentityHmac.Secret
    };

    private static void Validate(CloudLoginWebConfiguration configuration) =>
        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true);

    // ── The identity HMAC secret ──────────────────────────────────────────────

    [Fact]
    public void ValidConfiguration_Passes() => Validate(ValidConfiguration());

    [Fact]
    public void MissingIdentityHmacSecret_FailsStartup()
    {
        // Outside Aspire nothing supplies it, and CloudLogin will not invent one: a substituted
        // key resolves none of the rows written under the real one.
        CloudLoginWebConfiguration configuration = ValidConfiguration();
        configuration.IdentityHmacSecret = null;

        IdentityHmacSecretException exception = Assert.Throws<IdentityHmacSecretException>(
            () => Validate(configuration));

        Assert.Contains(IdentityKeyHasher.ConfigurationKey, exception.Message);
        Assert.Contains(IdentityKeyHasher.EnvironmentVariableName, exception.Message);
    }

    [Theory]
    [InlineData("c2hvcnQ=")]                                    // 5 bytes
    [InlineData("bm90LXF1aXRlLXRoaXJ0eS10d28tYnl0ZXMh")]        // 27 bytes
    [InlineData("!!! not base64 or hex !!!")]                   // malformed
    public void InvalidIdentityHmacSecret_FailsStartup(string secret)
    {
        CloudLoginWebConfiguration configuration = ValidConfiguration();
        configuration.IdentityHmacSecret = secret;

        Assert.Throws<IdentityHmacSecretException>(() => Validate(configuration));
    }

    [Fact]
    public void HmacSecret_IsNotRequiredWithoutAzureStorage()
    {
        // A host on its own in-memory ICloudLoginStore (the demos, the tests) has no identity
        // index to key, so the default configuration must not force a secret on it.
        Validate(new CloudLoginWebConfiguration());
    }

    // ── One Cosmos database per realm ─────────────────────────────────────────

    [Fact]
    public void NonDefaultRealm_GetsItsOwnDatabaseWithoutConfiguringOne()
    {
        CloudLoginWebConfiguration configuration = ValidConfiguration();
        configuration.Core = new CloudLoginCoreConfiguration { RealmId = "tenant-b" };

        Validate(configuration);

        Assert.NotEqual(CloudLoginCoreContainers.DefaultDatabaseId, configuration.Core.DatabaseId);
        Assert.Equal(CloudLoginCoreContainers.DatabaseIdFor("tenant-b"), configuration.Core.DatabaseId);
    }

    [Fact]
    public void AnExplicitDatabaseInsideTheRealmNamespace_FailsStartup()
    {
        // "Login…" is the namespace realm databases are derived into, so a hand-picked name in it
        // is another realm's database. Two realms sharing containers is worse than failing.
        CloudLoginWebConfiguration configuration = ValidConfiguration();
        configuration.Core = new CloudLoginCoreConfiguration
        {
            RealmId = "tenant-b",
            DatabaseId = CloudLoginCoreContainers.DatabaseIdFor("tenant-c")
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Validate(configuration));

        Assert.Contains("tenant-b", exception.Message);
    }

    [Fact]
    public void AnExplicitDatabaseOutsideTheNamespace_IsAllowed()
    {
        // A deployment that wants to name its own database still can; it just cannot squat inside
        // the derived namespace.
        CloudLoginWebConfiguration configuration = ValidConfiguration();
        configuration.Core = new CloudLoginCoreConfiguration { RealmId = "tenant-b", DatabaseId = "ContosoTenantB" };

        Validate(configuration);
    }

    [Fact]
    public void TheRealmsOwnDerivedDatabase_IsAllowedExplicitly()
    {
        CloudLoginWebConfiguration configuration = ValidConfiguration();
        configuration.Core = new CloudLoginCoreConfiguration
        {
            RealmId = "tenant-b",
            DatabaseId = CloudLoginCoreContainers.DatabaseIdFor("tenant-b")
        };

        Validate(configuration);
    }

}
