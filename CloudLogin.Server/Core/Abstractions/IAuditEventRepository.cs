using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>
/// Append-only persistence for the <c>AuditEvents</c> container. There is deliberately no update
/// or delete: retention is native Cosmos TTL and events are immutable once written.
/// </summary>
public interface IAuditEventRepository
{
    Task AppendAsync(AuditEventDocument auditEvent, CancellationToken cancellationToken = default);

    /// <summary>Events for one partition (one realm/subject/month), newest first.</summary>
    Task<List<AuditEventDocument>> GetPartitionAsync(string partitionKey, int maxCount = 100, CancellationToken cancellationToken = default);
}
