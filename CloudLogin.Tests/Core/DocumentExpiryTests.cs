using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class DocumentExpiryTests
{
    [Fact]
    public void Recompute_DerivesTtlFromAbsoluteExpiry()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LoginRequestDocument document = new() { ExpiresOn = now.AddSeconds(120) };

        DocumentExpiry.Recompute(document, now);

        Assert.NotNull(document.Ttl);
        Assert.InRange(document.Ttl!.Value, 119, 121);
    }

    [Fact]
    public void Recompute_OnLaterUpdate_ShrinksTtlInsteadOfExtending()
    {
        // Cosmos TTL counts from last modification; recomputation must re-derive the remaining
        // time from the unchanged absolute expiry so an update never extends the lifetime.
        DateTimeOffset created = DateTimeOffset.UtcNow;
        LoginRequestDocument document = new() { ExpiresOn = created.AddSeconds(600) };

        DocumentExpiry.Recompute(document, created);
        int originalTtl = document.Ttl!.Value;

        DocumentExpiry.Recompute(document, created.AddSeconds(500));
        int recomputedTtl = document.Ttl!.Value;

        Assert.InRange(recomputedTtl, 99, 101);
        Assert.True(recomputedTtl < originalTtl);
    }

    [Fact]
    public void Recompute_PastExpiry_WritesMinimalPositiveTtl()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LoginRequestDocument document = new() { ExpiresOn = now.AddMinutes(-5) };

        DocumentExpiry.Recompute(document, now);

        Assert.Equal(1, document.Ttl);
        Assert.True(DocumentExpiry.IsExpired(document, now));
    }

    [Fact]
    public void Recompute_NoExpiry_LeavesTtlNull()
    {
        WorkspaceAccessDocument membership = new() { Kind = WorkspaceAccessKinds.Membership };

        DocumentExpiry.Recompute(membership);

        Assert.Null(membership.Ttl);
        Assert.False(DocumentExpiry.IsExpired(membership));
    }

    [Fact]
    public void IsExpired_ValidatesInApplicationCode_BecauseCosmosDeletesAsynchronously()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CredentialDocument recovery = new()
        {
            Kind = CredentialKinds.Recovery,
            ExpiresOn = now.AddSeconds(-1),
            Ttl = 600 // Stale ttl from an earlier write; the absolute expiry must win.
        };

        Assert.True(DocumentExpiry.IsExpired(recovery, now));
    }

    [Fact]
    public void ExpiringContainers_AreTheOnesHoldingExpiringDocuments()
    {
        // The TTL-enabled container set is a deliberate contract: every container that can hold
        // an expiring document has DefaultTimeToLive = -1 provisioned; Users and Workspaces hold
        // only permanent documents and get none.
        Assert.Equal("/id", CloudLoginCoreContainers.UsersPartitionKey);
        Assert.Equal("/UserId", CloudLoginCoreContainers.CredentialsPartitionKey);
        Assert.Equal("/id", CloudLoginCoreContainers.WorkspacesPartitionKey);
        Assert.Equal("/WorkspaceId", CloudLoginCoreContainers.WorkspaceAccessPartitionKey);
        Assert.Equal("/FamilyId", CloudLoginCoreContainers.SessionsPartitionKey);
        Assert.Equal("/id", CloudLoginCoreContainers.LoginRequestsPartitionKey);
        Assert.Equal("/partitionKey", CloudLoginCoreContainers.AuditEventsPartitionKey);
    }

    [Fact]
    public async Task AuditEvents_CarryRetentionTtl()
    {
        InMemoryAuditEventRepository repository = new();
        CloudLoginCoreConfiguration configuration = new() { AuditRetention = TimeSpan.FromDays(400) };
        AngryMonkey.CloudLogin.Server.Core.Application.AuditLogger logger = new(repository, configuration);

        await logger.LogAsync("Login.Succeeded", Guid.NewGuid());

        AuditEventDocument auditEvent = Assert.Single(repository.Events);
        Assert.NotNull(auditEvent.Ttl);
        Assert.InRange(auditEvent.Ttl!.Value, (int)TimeSpan.FromDays(399).TotalSeconds, (int)TimeSpan.FromDays(401).TotalSeconds);
        Assert.NotNull(auditEvent.ExpiresOn);
    }

    [Fact]
    public void AuditPartitionKey_UsesRealmSubjectAndMonthBucket()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset timestamp = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal($"default|{userId}|202608", AuditEventDocument.BuildPartitionKey("default", userId.ToString(), timestamp));
        Assert.Equal("default|system|202608", AuditEventDocument.BuildPartitionKey("default", null, timestamp));
    }
}
