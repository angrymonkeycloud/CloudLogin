using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>
/// Base shape shared by every document in the CloudLogin core database.
/// <para>
/// Unlike the legacy single-container schema, core documents carry no type discriminator or
/// synthetic partition key: each document type lives in its own container and the partition key
/// is a real property of the document (<c>/id</c>, <c>/userId</c>, <c>/workspaceId</c>,
/// <c>/familyId</c>, or <c>/partitionKey</c> depending on the container).
/// </para>
/// </summary>
public abstract class CloudLoginCoreDocument
{
    /// <summary>The document id. Also the partition key for containers partitioned by <c>/id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The version of this document's persisted shape. Independent from the API version and the
    /// deployment version: bump only when the stored JSON layout changes.
    /// </summary>
    public int SchemaVersion { get; set; } = CloudLoginCoreSchema.CurrentVersion;

    /// <summary>Cosmos ETag, used for optimistic concurrency. Never exposed through any API.</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public static class CloudLoginCoreSchema
{
    /// <summary>Current storage schema version written to new and updated documents.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Contract for documents that expire through native Cosmos TTL.
/// <para>
/// Every expiring document must carry both a positive <see cref="Ttl"/> and an absolute
/// <see cref="ExpiresOn"/>. Cosmos TTL counts from the document's last modification, so a
/// plain update would silently extend the lifetime; <see cref="DocumentExpiry.Recompute"/> must
/// run before every write so the relative <c>ttl</c> always re-derives from the absolute expiry.
/// Application code must additionally validate <see cref="ExpiresOn"/> on read because Cosmos
/// deletes expired documents asynchronously.
/// </para>
/// </summary>
public interface IExpiringDocument
{
    /// <summary>Absolute UTC expiry. The single source of truth for the document's lifetime.</summary>
    DateTimeOffset? ExpiresOn { get; set; }

    /// <summary>
    /// Cosmos TTL in seconds, recomputed from <see cref="ExpiresOn"/> before every write.
    /// Null (omitted) or -1 on documents that never expire.
    /// </summary>
    int? Ttl { get; set; }
}

public static class DocumentExpiry
{
    /// <summary>
    /// Re-derives the relative Cosmos <c>ttl</c> from the absolute expiry so an update never
    /// accidentally extends the document's lifetime. An already-elapsed expiry still writes a
    /// minimal positive TTL (Cosmos rejects 0), and application reads must check
    /// <see cref="IsExpired"/> regardless because Cosmos deletion is asynchronous.
    /// </summary>
    public static void Recompute(IExpiringDocument document, DateTimeOffset? nowUtc = null)
    {
        if (document.ExpiresOn is not { } expiresOn)
        {
            document.Ttl = null;
            return;
        }

        DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
        double secondsRemaining = Math.Ceiling((expiresOn - now).TotalSeconds);

        document.Ttl = secondsRemaining >= 1 ? (int)Math.Min(secondsRemaining, int.MaxValue) : 1;
    }

    /// <summary>
    /// Whether the document is past its absolute expiry. Required on every read of an expiring
    /// document: Cosmos TTL deletion is asynchronous, so an expired document can still be returned.
    /// </summary>
    public static bool IsExpired(IExpiringDocument document, DateTimeOffset? nowUtc = null) =>
        document.ExpiresOn is { } expiresOn && expiresOn <= (nowUtc ?? DateTimeOffset.UtcNow);
}
