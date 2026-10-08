using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>Outcomes recorded on an audit event.</summary>
public static class AuditResults
{
    public const string Success = "Success";
    public const string Failure = "Failure";
    public const string Denied = "Denied";
}

/// <summary>Well-known event types, so the dashboard and the writers cannot drift apart on a name.</summary>
public static class AuditEventTypes
{
    public const string ApplicationBlocked = "Application.Blocked";
    public const string ApplicationUnblocked = "Application.Unblocked";
    public const string ApplicationForgotten = "Application.Forgotten";

    public const string SecretKeyCreated = "SecretKey.Created";
    public const string SecretKeyRevoked = "SecretKey.Revoked";

    public const string ClientAuthenticated = "Client.Authenticated";
    public const string ClientAuthenticationFailed = "Client.AuthenticationFailed";
    public const string TokenIssued = "Token.Issued";

    public const string LoginSucceeded = "Login.Succeeded";
    public const string LoginFailed = "Login.Failed";

    public const string TransactionStarted = "Transaction.Started";
    public const string TransactionRejected = "Transaction.Rejected";
    public const string TransactionCompleted = "Transaction.Completed";

    public const string SessionRevoked = "Session.Revoked";
    public const string SessionsRevokedForUser = "Session.RevokedForUser";
    public const string SessionsRevokedForApplication = "Session.RevokedForApplication";
    public const string UserSignedOutEverywhere = "User.SignedOutEverywhere";

    public const string UserDisabled = "User.Disabled";
    public const string UserEnabled = "User.Enabled";
    public const string DeviceRevoked = "Device.RevokedByAdmin";

    public const string AdminRolesChanged = "Admin.RolesChanged";
    public const string SigningKeyRotated = "Key.Rotated";
    public const string ProviderEnabled = "Provider.Enabled";
    public const string ProviderDisabled = "Provider.Disabled";

    public const string LogoutNotified = "Logout.Notified";
    public const string LogoutDeliveryFailed = "Logout.DeliveryFailed";
}

/// <summary>One event to record. Metadata is for small, non-secret details only.</summary>
public sealed class AuditEntry
{
    public required string EventType { get; init; }
    public Guid? UserId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ClientId { get; init; }
    public string Result { get; init; } = AuditResults.Success;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public IReadOnlyDictionary<string, string>? Data { get; init; }
}

/// <summary>Writes append-only security events. Failures never break the operation being audited.</summary>
public interface IAuditLogger
{
    Task LogAsync(string eventType, Guid? userId = null, Guid? actorUserId = null,
        string? ipAddress = null, string? userAgent = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);

    /// <summary>Records an event that can carry an application and a result.</summary>
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed class AuditLogger(
    IAuditEventRepository repository,
    CloudLoginCoreConfiguration configuration,
    ILogger<AuditLogger>? logger = null) : IAuditLogger
{
    private readonly IAuditEventRepository _repository = repository;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly ILogger<AuditLogger>? _logger = logger;

    public Task LogAsync(string eventType, Guid? userId = null, Guid? actorUserId = null,
        string? ipAddress = null, string? userAgent = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default) =>
        LogAsync(new AuditEntry
        {
            EventType = eventType,
            UserId = userId,
            ActorUserId = actorUserId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Data = data
        }, cancellationToken);

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // An event about a person is filed under the person; one about an application only, under
        // the application; anything else under the realm. Each trail then reads from few partitions.
        string? subject = entry.UserId?.ToString()
            ?? (string.IsNullOrWhiteSpace(entry.ClientId) ? null : AuditEventDocument.ApplicationSubject(entry.ClientId));

        AuditEventDocument auditEvent = new()
        {
            Id = Guid.NewGuid().ToString(),
            PartitionKey = AuditEventDocument.BuildPartitionKey(_configuration.RealmId, subject, now),
            Realm = _configuration.RealmId,
            EventType = entry.EventType,
            UserId = entry.UserId?.ToString(),
            ActorUserId = entry.ActorUserId?.ToString(),
            ClientId = entry.ClientId,
            Result = entry.Result,
            OccurredOn = now,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            Data = entry.Data is null ? null : new Dictionary<string, string>(entry.Data),
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
            _logger?.LogError(exception, "Failed to append audit event {EventType}.", entry.EventType);
        }
    }
}
