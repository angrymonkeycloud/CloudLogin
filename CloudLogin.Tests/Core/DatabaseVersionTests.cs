using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Versioning;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The storage-version axis: which schema a deployment runs on, how it defaults, and the
/// boundary that stops V2's legacy compatibility settings from silently doing nothing under V3.
/// </summary>
public class DatabaseVersionTests
{
    private static CloudLoginWebConfiguration ValidBase() => new()
    {
        Cosmos = new CosmosConfiguration { ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=key" },
        AzureStorage = new AzureStorageConfiguration { ConnectionString = "UseDevelopmentStorage=true" },
        IdentityHmacSecret = TestIdentityHmac.Secret
    };

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void NoConfiguration_SelectsV3AndMaterializesItsDefaults()
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.NormalizeVersions();

        Assert.Equal(CloudLoginDatabaseVersion.V3, configuration.DatabaseVersion);
        Assert.True(configuration.UsesCoreDatabase);

        // V3 needs no configuration: its settings are defaulted in so every consumer can read them.
        Assert.NotNull(configuration.Core);
        Assert.Equal(CloudLoginCoreContainers.DefaultDatabaseId, configuration.Core!.DatabaseId);
        Assert.Equal("Login", configuration.Core.DatabaseId);
    }

    [Fact]
    public void NormalizeVersions_IsIdempotent()
    {
        CloudLoginWebConfiguration configuration = new();

        configuration.NormalizeVersions();
        CloudLoginCoreConfiguration first = configuration.Core!;
        configuration.NormalizeVersions();

        Assert.Same(first, configuration.Core);
    }

    [Fact]
    public void V2_LeavesTheCoreUnconfigured()
    {
        CloudLoginWebConfiguration configuration = new() { DatabaseVersion = CloudLoginDatabaseVersion.V2 };
        configuration.NormalizeVersions();

        Assert.False(configuration.UsesCoreDatabase);
        Assert.Null(configuration.Core);
    }

    // ── V3: no legacy compatibility ───────────────────────────────────────────

    [Theory]
    [InlineData(nameof(CosmosConfiguration.IncludeLegacySchema))]
    [InlineData(nameof(CosmosConfiguration.SaveIdMode))]
    [InlineData(nameof(CosmosConfiguration.UserInfoPartitionKeyValue))]
    [InlineData(nameof(CosmosConfiguration.JsonCompatibilityMode))]
    public void V3_RejectsLegacySchemaSettings(string setting)
    {
        // Under V3 these would silently do nothing, so a deployment that sets them is told rather
        // than left believing they are in effect.
        CloudLoginWebConfiguration configuration = ValidBase();

        switch (setting)
        {
            case nameof(CosmosConfiguration.IncludeLegacySchema):
                configuration.Cosmos.IncludeLegacySchema = true;
                break;
            case nameof(CosmosConfiguration.SaveIdMode):
                configuration.Cosmos.SaveIdMode = IdSaveMode.TypePrefixed;
                break;
            case nameof(CosmosConfiguration.UserInfoPartitionKeyValue):
                configuration.Cosmos.UserInfoPartitionKeyValue = "User";
                break;
            case nameof(CosmosConfiguration.JsonCompatibilityMode):
                configuration.Cosmos.JsonCompatibilityMode = JsonCompatibilityMode.Legacy;
                break;
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true));

        Assert.Contains(setting, exception.Message);
        Assert.Contains("V2", exception.Message);
    }

    [Fact]
    public void V3_AcceptsTheLegacySettingsAtTheirDefaults()
    {
        // Only a deliberate non-default value is a conflict; the untouched defaults are not.
        CloudLoginConfigurationValidator.Validate(ValidBase(), isDevelopment: true);
    }

    [Fact]
    public void V3_RequiresAzureStorageForTheIdentityIndex()
    {
        CloudLoginWebConfiguration configuration = ValidBase();
        configuration.AzureStorage = null;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true));

        Assert.Contains("IdentityKeys", exception.Message);
    }

    [Fact]
    public void NoCosmosAtAll_IsValid_SoInMemoryHostsStillWork()
    {
        // A host running on its own ICloudLoginStore (the demos, tests) configures no Cosmos
        // account; V3 being the default must not force one on it.
        CloudLoginConfigurationValidator.Validate(new CloudLoginWebConfiguration(), isDevelopment: true);
    }

    // ── V2: names the database it reads ───────────────────────────────────────

    [Fact]
    public void V2_RequiresTheExistingDatabaseAndContainerToBeNamed()
    {
        CloudLoginWebConfiguration configuration = ValidBase();
        configuration.DatabaseVersion = CloudLoginDatabaseVersion.V2;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true));

        Assert.Contains("Cosmos:DatabaseId", exception.Message);
    }

    [Fact]
    public void V2_WithItsLegacySettings_IsValid()
    {
        CloudLoginWebConfiguration configuration = ValidBase();
        configuration.DatabaseVersion = CloudLoginDatabaseVersion.V2;
        configuration.Cosmos.DatabaseId = "Users";
        configuration.Cosmos.ContainerId = "Data";
        configuration.Cosmos.IncludeLegacySchema = true;
        configuration.Cosmos.SaveIdMode = IdSaveMode.TypePrefixed;
        configuration.Cosmos.UserInfoPartitionKeyValue = "User";

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true);

        Assert.False(configuration.UsesCoreDatabase);
    }

    [Fact]
    public void V2_WithCoreSettings_IsRejectedRatherThanIgnored()
    {
        CloudLoginWebConfiguration configuration = ValidBase();
        configuration.DatabaseVersion = CloudLoginDatabaseVersion.V2;
        configuration.Cosmos.DatabaseId = "Users";
        configuration.Cosmos.ContainerId = "Data";
        configuration.Core = new CloudLoginCoreConfiguration { RealmId = "tenant-a" };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true));

        Assert.Contains("DatabaseVersion", exception.Message);
    }

    [Fact]
    public void InvalidDatabaseVersion_FailsValidation()
    {
        CloudLoginWebConfiguration configuration = ValidBase();
        configuration.DatabaseVersion = (CloudLoginDatabaseVersion)99;

        Assert.Throws<InvalidOperationException>(
            () => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true));
    }

    // ── TTL contract ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CloudLoginCoreContainers.Credentials, true)]
    [InlineData(CloudLoginCoreContainers.WorkspaceAccess, true)]
    [InlineData(CloudLoginCoreContainers.Sessions, true)]
    [InlineData(CloudLoginCoreContainers.LoginRequests, true)]
    [InlineData(CloudLoginCoreContainers.AuditEvents, true)]
    [InlineData(CloudLoginCoreContainers.SigningKeysFallback, true)]
    [InlineData(CloudLoginCoreContainers.Users, false)]
    [InlineData(CloudLoginCoreContainers.Workspaces, false)]
    public void RequiresTimeToLive_MatchesTheContainersThatHoldExpiringDocuments(string container, bool expected)
    {
        // One list read by both provisioning paths — the runtime provisioner and the AppHost's
        // bicep. Cosmos ignores a document's own ttl when container TTL is off, so a container
        // that appears in one list but not the other silently stops expiring anything.
        Assert.Equal(expected, CloudLoginCoreContainers.RequiresTimeToLive(container));
    }

    // ── Storage numbering ─────────────────────────────────────────────────────

    [Fact]
    public void DatabaseVersions_StartAtV2_BecauseV1IsAnApiContractOnly()
    {
        Assert.Equal([CloudLoginDatabaseVersion.V2, CloudLoginDatabaseVersion.V3],
            Enum.GetValues<CloudLoginDatabaseVersion>());

        Assert.Equal(2, (int)CloudLoginDatabaseVersion.V2);
        Assert.Equal(3, (int)CloudLoginDatabaseVersion.V3);
    }
}
