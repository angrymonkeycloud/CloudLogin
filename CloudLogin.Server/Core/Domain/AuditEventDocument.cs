using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>
/// An append-only security event in the <c>AuditEvents</c> container (partition key
/// <c>/partitionKey</c>).
/// <para>
/// The partition key is <c>{realm}|{subject}|{yyyyMM}</c> — realm, then the affected user id (or
/// <c>system</c>), then a month bucket — so one user's trail reads from few partitions, no single
/// partition grows without bound, and a realm's events are enumerable month by month. Retention
/// is native Cosmos TTL from the configured audit retention; events are never updated or
/// deleted by application code.
/// </para>
/// </summary>
public sealed class AuditEventDocument : CloudLoginCoreDocument, IExpiringDocument
{
    /// <summary>Partition key: see <see cref="BuildPartitionKey"/>.</summary>
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>Dotted event name, for example <c>Login.Succeeded</c> or <c>Session.ReuseDetected</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    public string Realm { get; set; } = string.Empty;

    /// <summary>The affected user, when the event concerns one.</summary>
    public string? UserId { get; set; }

    /// <summary>The acting user when different from the affected user (admin operations).</summary>
    public string? ActorUserId { get; set; }

    public DateTimeOffset OccurredOn { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Small, non-secret event details. Never credentials, hashes, tokens, or codes.</summary>
    public Dictionary<string, string>? Data { get; set; }

    public DateTimeOffset? ExpiresOn { get; set; }

    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    public const string SystemSubject = "system";

    public static string BuildPartitionKey(string realm, string? userId, DateTimeOffset timestamp) =>
        $"{realm}|{(string.IsNullOrEmpty(userId) ? SystemSubject : userId)}|{timestamp:yyyyMM}";
}
