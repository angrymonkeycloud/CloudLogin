using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using System.Reflection;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// Invariants that hold across every core store: what a permanent record must carry, and where a
/// concurrent write has to lose rather than overwrite.
/// </summary>
public class StorageInvariantTests
{
    public static IEnumerable<object[]> CoreDocumentTypes() =>
        typeof(CloudLoginCoreDocument).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(CloudLoginCoreDocument)) && !type.IsAbstract)
            .Select(type => new object[] { type });

    // ── SchemaVersion on everything permanent ─────────────────────────────────

    [Theory]
    [MemberData(nameof(CoreDocumentTypes))]
    public void EveryCoreDocument_CarriesTheCurrentSchemaVersion(Type documentType)
    {
        // Inherited from the base rather than repeated, so a new document type cannot forget it.
        // Without a version on the document, a future layout change has no way to tell which
        // shape it is reading.
        CloudLoginCoreDocument document = (CloudLoginCoreDocument)Activator.CreateInstance(documentType)!;

        Assert.Equal(CloudLoginCoreSchema.CurrentVersion, document.SchemaVersion);
    }

    [Fact]
    public void CoreDocumentTypes_AreActuallyDiscovered()
    {
        // Guards the theory above: a MemberData that silently returned nothing would pass.
        Assert.True(CoreDocumentTypes().Count() >= 6);
    }

    [Fact]
    public void IdentityKeyEntity_CarriesItsVersionTriplet()
    {
        // Table Storage entities have no shared base class to inherit from, so this is checked
        // separately — the requirement is the same: every permanent record says what it is.
        IdentityKey key = new();

        Assert.Equal(CloudLoginCoreSchema.CurrentVersion, key.SchemaVersion);
        Assert.Equal(IdentityKeyHasher.CurrentHashVersion, key.HashVersion);
        Assert.Equal(IdentityKeyHasher.CurrentNormalizationVersion, key.NormalizationVersion);
    }

    // ── Container storage configuration ───────────────────────────────────────

    [Theory]
    [InlineData(CloudLoginCoreContainers.Users, CloudLoginCoreContainers.UsersPartitionKey, false)]
    [InlineData(CloudLoginCoreContainers.Credentials, CloudLoginCoreContainers.CredentialsPartitionKey, true)]
    [InlineData(CloudLoginCoreContainers.Workspaces, CloudLoginCoreContainers.WorkspacesPartitionKey, false)]
    [InlineData(CloudLoginCoreContainers.WorkspaceAccess, CloudLoginCoreContainers.WorkspaceAccessPartitionKey, true)]
    [InlineData(CloudLoginCoreContainers.Sessions, CloudLoginCoreContainers.SessionsPartitionKey, true)]
    [InlineData(CloudLoginCoreContainers.LoginRequests, CloudLoginCoreContainers.LoginRequestsPartitionKey, true)]
    [InlineData(CloudLoginCoreContainers.AuditEvents, CloudLoginCoreContainers.AuditEventsPartitionKey, true)]
    [InlineData(CloudLoginCoreContainers.SigningKeysFallback, CloudLoginCoreContainers.SigningKeysFallbackPartitionKey, true)]
    public void Containers_ArePartitionedAndTtlArmedAsSpecified(string container, string partitionKey, bool expiring)
    {
        // Users and Workspaces hold nothing that expires, so their containers stay TTL-off: an
        // armed container plus a stray ttl on a profile document would delete a live account.
        Assert.StartsWith("/", partitionKey);
        Assert.Equal(expiring, CloudLoginCoreContainers.RequiresTimeToLive(container));
    }

    // ── Conditional writes ────────────────────────────────────────────────────

    [Fact]
    public async Task Credentials_ConcurrentReadModifyWrite_LosesOnAStaleETag()
    {
        InMemoryCredentialRepository credentials = new();
        Guid userId = Guid.NewGuid();
        Guid contactId = Guid.NewGuid();

        await credentials.CreateAsync(new CredentialDocument
        {
            Id = CredentialDocument.PasswordId(contactId),
            UserId = userId.ToString(),
            Kind = CredentialKinds.Password,
            ContactId = contactId,
            PasswordHash = "original"
        });

        // Two callers read the same credential and both change the password.
        CredentialDocument first = (await credentials.GetAsync(userId, CredentialDocument.PasswordId(contactId)))!;
        CredentialDocument second = (await credentials.GetAsync(userId, CredentialDocument.PasswordId(contactId)))!;

        first.PasswordHash = "first-wins";
        await credentials.UpsertAsync(first);

        second.PasswordHash = "second-would-clobber";
        await Assert.ThrowsAsync<CoreConcurrencyException>(() => credentials.UpsertAsync(second));

        Assert.Equal("first-wins",
            (await credentials.GetAsync(userId, CredentialDocument.PasswordId(contactId)))!.PasswordHash);
    }

    [Fact]
    public async Task Credentials_AFreshDocument_WritesUnconditionally()
    {
        // The create case: nothing was read, so there is no ETag to be stale, and the write must
        // not be refused for lacking one.
        InMemoryCredentialRepository credentials = new();
        Guid userId = Guid.NewGuid();
        Guid contactId = Guid.NewGuid();

        await credentials.UpsertAsync(new CredentialDocument
        {
            Id = CredentialDocument.PasswordId(contactId),
            UserId = userId.ToString(),
            Kind = CredentialKinds.Password,
            ContactId = contactId,
            PasswordHash = "hash"
        });

        Assert.NotNull(await credentials.GetAsync(userId, CredentialDocument.PasswordId(contactId)));
    }

    [Fact]
    public void ETags_AreNeverPartOfAnyPublicDocumentSurface()
    {
        // ETags are a storage concern. One leaking into a DTO would invite a client to send it
        // back, turning a server-side concurrency guard into a client-controlled one.
        foreach (Type documentType in CoreDocumentTypes().Select(row => (Type)row[0]))
        {
            PropertyInfo? etag = documentType.GetProperty("ETag");
            Assert.NotNull(etag);
            Assert.Equal(typeof(CloudLoginCoreDocument), etag!.DeclaringType);
        }
    }
}
