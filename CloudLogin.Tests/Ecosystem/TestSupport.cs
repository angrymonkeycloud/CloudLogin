using System.Collections.Concurrent;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Tests.Core;

namespace AngryMonkey.CloudLogin.Tests;

internal sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan span) => _now += span;
}

/// <summary>
/// A login-request repository that also sees the one-time requests the in-memory authority store issues, so a test can
/// follow a request id from creation to redemption.
/// </summary>
internal sealed class StoreBackedLoginRequestRepository(InMemoryCloudLoginStore store) : ILoginRequestRepository
{
    public ConcurrentDictionary<string, LoginRequestDocument> Documents { get; } = new();

    public Task<LoginRequestDocument?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!Documents.ContainsKey(requestId) && Guid.TryParse(requestId, out Guid id) && store.Requests.TryGetValue(id, out Guid userId))
            Documents[requestId] = new LoginRequestDocument { Id = requestId, Kind = LoginRequestKinds.Login, UserId = userId.ToString(), ETag = "e1", ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5) };

        return Task.FromResult(Documents.TryGetValue(requestId, out LoginRequestDocument? document) ? TestClone.Clone(document) : null);
    }

    public Task CreateAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        request.ETag = "e1";

        if (!Documents.TryAdd(request.Id, TestClone.Clone(request)))
            throw new CoreConflictException("exists");

        return Task.CompletedTask;
    }

    public Task<bool> TryReplaceAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        Documents[request.Id] = TestClone.Clone(request);
        return Task.FromResult(true);
    }

    public Task<bool> TryDeleteAsync(LoginRequestDocument request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.TryRemove(request.Id, out _));

    public Task<LoginRequestDocument?> FindByUserCodeHashAsync(string userCodeHash, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoginRequestDocument?>(null);
}

/// <summary>A distributed cache that can be made to fail writes, for proving that a failed write is retried rather than acknowledged.</summary>
internal sealed class FaultableCache(Microsoft.Extensions.Caching.Distributed.IDistributedCache inner) : Microsoft.Extensions.Caching.Distributed.IDistributedCache
{
    private int _failures;

    /// <summary>Which keys fail, by prefix, for the next <see cref="FailNext"/> writes. Empty means any key.</summary>
    public string KeyPrefix { get; private set; } = string.Empty;

    public int Failed { get; private set; }

    public void FailNext(int count, string keyPrefix = "")
    {
        KeyPrefix = keyPrefix;
        Interlocked.Exchange(ref _failures, count);
    }

    private void MaybeFail(string key)
    {
        if (key.StartsWith(KeyPrefix, StringComparison.Ordinal) && Volatile.Read(ref _failures) > 0 && Interlocked.Decrement(ref _failures) >= 0)
        {
            Failed++;
            throw new InvalidOperationException("The cache is unavailable.");
        }
    }

    public byte[]? Get(string key) => inner.Get(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);

    public void Refresh(string key) => inner.Refresh(key);

    public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

    public void Remove(string key) => inner.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);

    public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options)
    {
        MaybeFail(key);
        inner.Set(key, value, options);
    }

    public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        MaybeFail(key);
        return inner.SetAsync(key, value, options, token);
    }
}
