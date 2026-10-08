using System.Collections.Concurrent;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using AngryMonkey.CloudLogin.Tests.Ecosystem;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Tests.Core;

internal sealed class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly object _lock = new();

    public ConcurrentDictionary<string, ApplicationDocument> Documents { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes reads fail the way an unreachable registry does.</summary>
    public bool Unavailable { get; set; }

    public Task<ApplicationDocument?> GetAsync(string clientId, CancellationToken cancellationToken = default) =>
        Unavailable ? throw new InvalidOperationException("The registry is unavailable.") :
        Task.FromResult(Documents.TryGetValue(clientId, out ApplicationDocument? application) ? TestClone.Clone(application) : null);

    public Task<List<ApplicationDocument>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Unavailable ? throw new InvalidOperationException("The registry is unavailable.") :
        Task.FromResult(Documents.Values.Select(TestClone.Clone).ToList());

    public Task CreateAsync(ApplicationDocument application, CancellationToken cancellationToken = default)
    {
        ApplicationDocument copy = TestClone.Clone(application);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd(copy.Id, copy))
            throw new CoreConflictException("Application exists.");

        application.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(ApplicationDocument application, CancellationToken cancellationToken = default)
    {
        if (!Replace(application))
            throw new CoreConcurrencyException("ETag mismatch.");

        return Task.CompletedTask;
    }

    public Task<bool> TryReplaceAsync(ApplicationDocument application, CancellationToken cancellationToken = default) =>
        Task.FromResult(Replace(application));

    private bool Replace(ApplicationDocument application)
    {
        lock (_lock)
        {
            if (!Documents.TryGetValue(application.Id, out ApplicationDocument? current)
                || !string.Equals(current.ETag, application.ETag, StringComparison.Ordinal))
                return false;

            ApplicationDocument copy = TestClone.Clone(application);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[application.Id] = copy;
            application.ETag = copy.ETag;
            return true;
        }
    }

    public Task DeleteAsync(string clientId, CancellationToken cancellationToken = default)
    {
        Documents.TryRemove(clientId, out _);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySecretKeyRepository : ISecretKeyRepository
{
    public ConcurrentDictionary<string, SecretKeyDocument> Documents { get; } = new(StringComparer.Ordinal);

    /// <summary>Makes reads fail the way an unreachable registry does.</summary>
    public bool Unavailable { get; set; }

    public Task<SecretKeyDocument?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Unavailable ? throw new InvalidOperationException("The registry is unavailable.") :
        Task.FromResult(Documents.TryGetValue(id, out SecretKeyDocument? key) ? TestClone.Clone(key) : null);

    public Task<List<SecretKeyDocument>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Unavailable ? throw new InvalidOperationException("The registry is unavailable.") :
        Task.FromResult(Documents.Values.Select(TestClone.Clone).ToList());

    public Task CreateAsync(SecretKeyDocument key, CancellationToken cancellationToken = default)
    {
        SecretKeyDocument copy = TestClone.Clone(key);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd(copy.Id, copy))
            throw new CoreConflictException("Key exists.");

        key.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    public Task<bool> TryReplaceAsync(SecretKeyDocument key, CancellationToken cancellationToken = default)
    {
        lock (Documents)
        {
            if (!Documents.TryGetValue(key.Id, out SecretKeyDocument? current) || !string.Equals(current.ETag, key.ETag, StringComparison.Ordinal))
                return Task.FromResult(false);

            SecretKeyDocument copy = TestClone.Clone(key);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[key.Id] = copy;
            key.ETag = copy.ETag;
            return Task.FromResult(true);
        }
    }
}

/// <summary>The control plane wired over in-memory storage, the way the host wires it over Cosmos.</summary>
internal sealed class ControlPlaneFixture
{
    public const string DeploymentKey = "deployment-secret-key-for-the-tests-0123456789";

    public Guid Admin { get; } = Guid.NewGuid();

    /// <summary>The clock the directory and services read, so a test can let caches age without waiting.</summary>
    public TestTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public InMemoryAuditEventRepository AuditEvents { get; } = new();
    public InMemoryApplicationRepository Applications { get; } = new();
    public InMemorySecretKeyRepository SecretKeyStore { get; } = new();
    public InMemorySessionRepository Sessions { get; } = new();
    public InMemoryUserRepository Users { get; } = new();
    public CloudLoginTokenOptions Options { get; }
    public AuditLogger Audit { get; }
    public SessionService SessionService { get; }
    public ClientDirectory Directory { get; }
    public ApplicationService Apps { get; }
    public SecretKeyService Keys { get; }

    public ControlPlaneFixture(Action<CloudLoginTokenOptions>? configure = null)
    {
        Options = new CloudLoginTokenOptions { Issuer = "https://login.example.test", SecretKeys = [DeploymentKey] };
        configure?.Invoke(Options);

        CloudLoginCoreConfiguration core = new();
        IOptions<CloudLoginTokenOptions> wrapped = Microsoft.Extensions.Options.Options.Create(Options);

        Audit = new AuditLogger(AuditEvents, core);
        SessionService = new SessionService(Sessions, core, Audit);
        Directory = new ClientDirectory(wrapped, Applications, SecretKeyStore, Audit, null, Clock);
        Apps = new ApplicationService(Applications, Sessions, SessionService, Directory, Audit);
        Keys = new SecretKeyService(SecretKeyStore, Directory, Audit, wrapped, Clock);
    }

    /// <summary>A new process over the same storage: fresh caches, the same keys and applications.</summary>
    public ClientDirectory Restart(Action<CloudLoginTokenOptions>? configure = null)
    {
        CloudLoginTokenOptions options = new() { Issuer = Options.Issuer, SecretKeys = [.. Options.SecretKeys] };
        configure?.Invoke(options);
        return new ClientDirectory(Microsoft.Extensions.Options.Options.Create(options), Applications, SecretKeyStore, Audit, null, Clock);
    }

    public Task<ClientAuthenticationResult> AuthenticateAsync(string clientId, string secret) => Directory.AuthenticateAsync(clientId, secret);

    public IEnumerable<AuditEventDocument> Events(string eventType) => AuditEvents.Events.Where(item => item.EventType == eventType);
}
