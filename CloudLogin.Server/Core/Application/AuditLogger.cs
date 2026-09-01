using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>Writes append-only security events. Failures never break the operation being audited.</summary>
public interface IAuditLogger
{
    Task LogAsync(string eventType, Guid? userId = null, Guid? actorUserId = null,
        string? ipAddress = null, string? userAgent = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}

public sealed class AuditLogger(
    IAuditEventRepository repository,
    CloudLoginCoreConfiguration configuration,
    ILogger<AuditLogger>? logger = null) : IAuditLogger
{
    private readonly IAuditEventRepository _repository = repository;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly ILogger<AuditLogger>? _logger = logger;

    public async Task LogAsync(string eventType, Guid? userId = null, Guid? actorUserId = null,
        string? ipAddress = null, string? userAgent = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        AuditEventDocument auditEvent = new()
        {
            Id = Guid.NewGuid().ToString(),
            PartitionKey = AuditEventDocument.BuildPartitionKey(_configuration.RealmId, userId?.ToString(), now),
            Realm = _configuration.RealmId,
            EventType = eventType,
            UserId = userId?.ToString(),
            ActorUserId = actorUserId?.ToString(),
            OccurredOn = now,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Data = data is null ? null : new Dictionary<string, string>(data),
            ExpiresOn = now + _configuration.AuditRetention
        };

        DocumentExpiry.Recompute(auditEvent, now);

        try
        {
            await _repository.AppendAsync(auditEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            // Auditing is best-effort by design: a storage hiccup must not fail a sign-in.
            _logger?.LogError(exception, "Failed to append audit event {EventType}.", eventType);
        }
    }
}
