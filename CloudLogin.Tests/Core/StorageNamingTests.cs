using System.Text.RegularExpressions;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// CloudLogin's Azure Storage resources are named so their owner is obvious in an account shared
/// with the rest of a product, and so the service will actually accept them.
/// <para>
/// Both halves matter. An unprefixed <c>IdentityKeys</c> beside another component's tables gives
/// nobody a clue who owns it; and an illegal name is not a compile error — it surfaces at
/// runtime, on first use, as a failed sign-in.
/// </para>
/// </summary>
public partial class StorageNamingTests
{
    // Azure Table Storage: alphanumeric only, must start with a letter, 3-63 characters.
    // Hyphens are NOT permitted, which is why tables use "Login…" and not "login-…".
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{2,62}$")]
    private static partial Regex LegalTableName();

    // Blob containers: lowercase letters, digits and hyphens; must start and end alphanumeric;
    // no consecutive hyphens; 3-63 characters. Hyphens ARE permitted, so containers use "login-".
    [GeneratedRegex("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex LegalContainerName();

    public static TheoryData<string> TableNames() =>
    [
        CloudLoginCoreContainers.IdentityKeysTable,
        CloudLoginCoreContainers.UserWorkspaceIndexTable
    ];

    [Theory]
    [MemberData(nameof(TableNames))]
    public void Tables_AreLoginPrefixed_AndLegal(string tableName)
    {
        Assert.StartsWith("Login", tableName, StringComparison.Ordinal);

        Assert.Matches(LegalTableName(), tableName);

        // Stated explicitly so nobody "fixes" the missing hyphen to match the container style:
        // Azure rejects it outright.
        Assert.DoesNotContain('-', tableName);
    }

    [Fact]
    public void BlobContainer_IsLoginHyphenPrefixed_AndLegal()
    {
        string containerName = new AzureStorageConfiguration().ContainerName;

        Assert.StartsWith("login-", containerName, StringComparison.Ordinal);
        Assert.Matches(LegalContainerName(), containerName);
    }

    [Fact]
    public void BlobContainer_KeepsItsHyphen_BecauseContainerNamesAllowOne()
    {
        // The paired half of the table assertion: where the naming rules permit a hyphen, the
        // readable form is the one used.
        Assert.Contains('-', new AzureStorageConfiguration().ContainerName);
    }

    [Fact]
    public void PublicBaseUrl_FollowsTheContainerName()
    {
        // The container name reaches the outside world through profile-picture URLs, so a rename
        // has to carry through here too rather than leaving links pointing at the old container.
        AzureStorageConfiguration storage = new()
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=example;AccountKey=key;"
        };

        Assert.EndsWith($"/{storage.ContainerName}/", storage.PublicBaseUrl);
    }
}
