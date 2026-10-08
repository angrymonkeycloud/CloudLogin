using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Abstractions;

/// <summary>Persistence for the <c>Applications</c> container. The client id is the document id.</summary>
public interface IApplicationRepository
{
    Task<ApplicationDocument?> GetAsync(string clientId, CancellationToken cancellationToken = default);

    Task<List<ApplicationDocument>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> when the client id exists.</summary>
    Task CreateAsync(ApplicationDocument application, CancellationToken cancellationToken = default);

    /// <summary>ETag-guarded. Throws <see cref="CoreConcurrencyException"/> when it changed meanwhile.</summary>
    Task ReplaceAsync(ApplicationDocument application, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="ReplaceAsync"/> but reports a lost race as <see langword="false"/>.</summary>
    Task<bool> TryReplaceAsync(ApplicationDocument application, CancellationToken cancellationToken = default);

    Task DeleteAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence for the <c>SecretKeys</c> container. The key id is the document id.</summary>
public interface ISecretKeyRepository
{
    Task<SecretKeyDocument?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<List<SecretKeyDocument>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Create-only. Throws <see cref="CoreConflictException"/> when the id exists.</summary>
    Task CreateAsync(SecretKeyDocument key, CancellationToken cancellationToken = default);

    /// <summary>Like a guarded replace, but reports a lost race as <see langword="false"/>.</summary>
    Task<bool> TryReplaceAsync(SecretKeyDocument key, CancellationToken cancellationToken = default);
}

/// <summary>What an audit search may filter on. Everything is optional; unset means no restriction.</summary>
public sealed class AuditEventQuery
{
    /// <summary>Exact event types, for example <c>Client.AuthenticationFailed</c>.</summary>
    public IReadOnlyList<string> EventTypes { get; init; } = [];

    /// <summary>Event types starting with this, for example <c>Application.</c>.</summary>
    public string? EventTypePrefix { get; init; }

    public string? ClientId { get; init; }
    public string? UserId { get; init; }
    public string? ActorUserId { get; init; }
    public string? Result { get; init; }

    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    public int Take { get; init; } = 100;

    public const int MaxTake = 500;
}

/// <summary>One provider's linked-identity count.</summary>
public sealed record ProviderIdentityCount(string ProviderCode, int Count);
