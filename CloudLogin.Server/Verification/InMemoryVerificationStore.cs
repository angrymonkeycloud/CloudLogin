using System.Collections.Concurrent;

namespace AngryMonkey.CloudLogin.Server.Verification;

/// <summary>
/// Holds verification challenges in this process. The fallback for a deployment with no core
/// database - a demo, a test host, or a single-instance development run.
/// </summary>
/// <remarks>
/// Correct but not shared: a second instance of the application has its own copy, so a code issued
/// by one server is unknown to the other. Any deployment running more than one instance gets the
/// core store instead, which is registered ahead of this one whenever the database is configured.
/// </remarks>
public sealed class InMemoryVerificationStore : ICloudLoginVerificationStore
{
    private readonly ConcurrentDictionary<string, Entry> _challenges = new(StringComparer.Ordinal);

    public Task CreateAsync(VerificationChallenge challenge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        Prune();

        challenge.ConcurrencyToken = Guid.NewGuid().ToString("N");

        if (!_challenges.TryAdd(challenge.Id, new Entry(Copy(challenge), challenge.ConcurrencyToken)))
            throw new InvalidOperationException("A verification challenge with the same id already exists.");

        return Task.CompletedTask;
    }

    public Task<VerificationChallenge?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_challenges.TryGetValue(id, out Entry? entry))
            return Task.FromResult<VerificationChallenge?>(null);

        VerificationChallenge challenge = Copy(entry.Challenge);
        challenge.ConcurrencyToken = entry.Token;

        return Task.FromResult<VerificationChallenge?>(challenge);
    }

    public Task<bool> TryUpdateAsync(VerificationChallenge challenge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        if (!_challenges.TryGetValue(challenge.Id, out Entry? current) || current.Token != challenge.ConcurrencyToken)
            return Task.FromResult(false);

        string token = Guid.NewGuid().ToString("N");
        Entry updated = new(Copy(challenge), token);

        if (!_challenges.TryUpdate(challenge.Id, updated, current))
            return Task.FromResult(false);

        challenge.ConcurrencyToken = token;

        return Task.FromResult(true);
    }

    /// <summary>Drops expired challenges, which nothing else would ever remove here.</summary>
    private void Prune()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (KeyValuePair<string, Entry> entry in _challenges)
        {
            if (entry.Value.Challenge.ExpiresOn <= now)
                _challenges.TryRemove(entry.Key, out _);
        }
    }

    private static VerificationChallenge Copy(VerificationChallenge challenge) => new()
    {
        Id = challenge.Id,
        CodeHash = challenge.CodeHash,
        Address = challenge.Address,
        Purpose = challenge.Purpose,
        CreatedOn = challenge.CreatedOn,
        ExpiresOn = challenge.ExpiresOn,
        State = challenge.State,
        AttemptCount = challenge.AttemptCount,
        UserId = challenge.UserId
    };

    private sealed record Entry(VerificationChallenge Challenge, string Token);
}
