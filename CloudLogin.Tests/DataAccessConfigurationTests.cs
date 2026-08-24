using AngryMonkey.CloudLogin.Server;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// How CloudLogin decides to authenticate against Cosmos and Azure Storage. Both a key and a
/// credential are supported, and which one a deployment gets is decided entirely by what it
/// configures - so these assert the choice, not the clients it produces.
/// </summary>
public class DataAccessConfigurationTests
{
    private const string StorageConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=examplestore;AccountKey=a2V5;EndpointSuffix=core.windows.net";

    private static IConfigurationSection Section(string name, Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => $"{name}:{pair.Key}", pair => pair.Value))
            .Build()
            .GetSection(name);

    // ── Azure Storage ────────────────────────────────────────────────────────

    [Fact]
    public void Storage_IsNotConfigured_UntilOneModeIsGiven()
    {
        AzureStorageConfiguration storage = new();

        Assert.False(storage.IsValid());
        Assert.Throws<InvalidOperationException>(storage.CreateContainerClient);
    }

    [Fact]
    public void Storage_BindsAnAccountNameWithoutAConnectionString()
    {
        // The shape a host supplies when the application reaches storage as its own identity: an
        // account name and nothing secret. The old binder threw here, which is what made
        // credential-based access impossible to configure.
        AzureStorageConfiguration storage = new(Section("Storage", new() { ["AccountName"] = "examplestore" }));

        Assert.True(storage.IsValid());
        Assert.Equal("examplestore", storage.AccountName);
        Assert.Null(storage.ConnectionString);
    }

    [Fact]
    public void Storage_ReachesTheAccountByCredential_WhenOneIsSupplied()
    {
        AzureStorageConfiguration storage = new()
        {
            AccountName = "examplestore",
            Credential = new StubCredential()
        };

        BlobContainerClient container = storage.CreateContainerClient();

        Assert.Equal("examplestore", container.AccountName);
        Assert.Equal("users", container.Name);
    }

    [Fact]
    public void Storage_StillReachesTheAccountByKey_WhenThatIsAllItHas()
    {
        AzureStorageConfiguration storage = new() { ConnectionString = StorageConnectionString };

        Assert.Equal("examplestore", storage.CreateContainerClient().AccountName);
    }

    [Fact]
    public void Storage_PrefersTheCredential_WhenBothAreConfigured()
    {
        // Naming an account and handing over a credential is a deliberate act; a connection string
        // is often inherited from a file nobody has revisited.
        AzureStorageConfiguration storage = new()
        {
            AccountName = "identitystore",
            ConnectionString = StorageConnectionString,
            Credential = new StubCredential()
        };

        Assert.Equal("identitystore", storage.CreateContainerClient().AccountName);
    }

    [Fact]
    public void Storage_DerivesItsPublicUrlFromTheAccountName_WhenThereIsNoConnectionString()
    {
        AzureStorageConfiguration storage = new() { AccountName = "examplestore", ContainerName = "users" };

        Assert.Equal("https://examplestore.blob.core.windows.net/users/", storage.PublicBaseUrl);
    }

    [Fact]
    public void Storage_StillDerivesItsPublicUrlFromAConnectionString()
    {
        AzureStorageConfiguration storage = new() { ConnectionString = StorageConnectionString };

        Assert.Equal("https://examplestore.blob.core.windows.net/users/", storage.PublicBaseUrl);
    }

    [Fact]
    public void Storage_HasNoPublicUrl_WhenNothingIsConfigured()
    {
        Assert.Null(new AzureStorageConfiguration().PublicBaseUrl);
    }

    [Fact]
    public void Storage_TreatsABareBlobEndpointAsAnAccountName()
    {
        // What Aspire publishes as the "connection string" of a storage account provisioned for
        // credential access: there is no key to put in one, so it is the blob endpoint alone.
        // Handing that to the connection-string parser is what used to stop a deployed site booting.
        AzureStorageConfiguration storage = AngryMonkey.CloudLogin.Aspire.CloudLoginAspireExtensions.BuildStorage(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "https://examplestore.blob.core.windows.net/"
                })
                .Build(),
            new StubCredential());

        Assert.Equal("examplestore", storage.AccountName);
        Assert.Null(storage.ConnectionString);
        Assert.NotNull(storage.Credential);
    }

    [Fact]
    public void Cosmos_TreatsABareAccountEndpointAsAnEndpoint()
    {
        CosmosConfiguration cosmos = AngryMonkey.CloudLogin.Aspire.CloudLoginAspireExtensions.BuildCosmos(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cosmos:ConnectionString"] = "https://example.documents.azure.com:443/"
                })
                .Build(),
            new StubCredential());

        Assert.Equal("https://example.documents.azure.com:443/", cosmos.AccountEndpoint);
        Assert.Null(cosmos.ConnectionString);
        Assert.NotNull(cosmos.Credential);
    }

    [Fact]
    public void ARealConnectionString_IsNeverMistakenForAnEndpoint()
    {
        // The distinguishing feature is the key=value pairs, not the scheme - a connection string
        // names an endpoint inside it, and must keep being treated as a connection string.
        CosmosConfiguration cosmos = AngryMonkey.CloudLogin.Aspire.CloudLoginAspireExtensions.BuildCosmos(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cosmos:ConnectionString"] = "AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=a2V5;"
                })
                .Build(),
            new StubCredential());

        Assert.Null(cosmos.AccountEndpoint);
        Assert.Null(cosmos.Credential);
        Assert.StartsWith("AccountEndpoint=", cosmos.ConnectionString);
    }

    // ── Cosmos ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cosmos_IsNotConfigured_UntilOneModeIsGiven()
    {
        CosmosConfiguration cosmos = new();

        Assert.False(cosmos.IsValid());
        Assert.Throws<InvalidOperationException>(cosmos.CreateClient);
    }

    [Fact]
    public void Cosmos_CountsAnAccountEndpointAsConfigured()
    {
        CosmosConfiguration cosmos = new(Section("Cosmos", new()
        {
            ["AccountEndpoint"] = "https://example.documents.azure.com:443/"
        }));

        Assert.True(cosmos.IsValid());
        Assert.Equal("https://example.documents.azure.com:443/", cosmos.AccountEndpoint);
    }

    [Fact]
    public void Cosmos_ReachesTheAccountByCredential_WhenOneIsSupplied()
    {
        CosmosConfiguration cosmos = new()
        {
            AccountEndpoint = "https://example.documents.azure.com:443/",
            Credential = new StubCredential()
        };

        using Microsoft.Azure.Cosmos.CosmosClient client = cosmos.CreateClient();

        Assert.Equal("https://example.documents.azure.com/", client.Endpoint.AbsoluteUri);
    }

    [Fact]
    public void Cosmos_StillReachesTheAccountByKey_WhenThatIsAllItHas()
    {
        CosmosConfiguration cosmos = new()
        {
            ConnectionString = "AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=" +
                Convert.ToBase64String("cosmos-account-key-placeholder"u8.ToArray()) + ";"
        };

        using Microsoft.Azure.Cosmos.CosmosClient client = cosmos.CreateClient();

        Assert.Equal("https://example.documents.azure.com/", client.Endpoint.AbsoluteUri);
    }

    [Fact]
    public void Cosmos_AlwaysCarriesTheRepositorysOwnSerializer()
    {
        // Directory.Build.props disables the Cosmos SDK's Newtonsoft check on the grounds that every
        // client in this repository uses ConfigurableCosmosSerializer. A client built without it
        // fails at runtime, not at build, so the guarantee is worth asserting.
        CosmosConfiguration cosmos = new()
        {
            AccountEndpoint = "https://example.documents.azure.com:443/",
            Credential = new StubCredential()
        };

        using Microsoft.Azure.Cosmos.CosmosClient client = cosmos.CreateClient();

        Assert.IsType<AngryMonkey.CloudLogin.Server.Serialization.ConfigurableCosmosSerializer>(
            client.ClientOptions.Serializer);
    }

    /// <summary>Never asked for a token: these tests assert which path is taken, not that it authenticates.</summary>
    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The tests never authenticate.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The tests never authenticate.");
    }
}
