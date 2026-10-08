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

    /// <summary>
    /// Events across every partition matching the query, newest first. Cross-partition by nature:
    /// it is how an administrator reads the realm's trail, not how a sign-in runs.
    /// </summary>
    Task<List<AuditEventDocument>> QueryAsync(AuditEventQuery query, CancellationToken cancellationToken = default);

    Task<int> CountAsync(AuditEventQuery query, CancellationToken cancellationToken = default);
}
