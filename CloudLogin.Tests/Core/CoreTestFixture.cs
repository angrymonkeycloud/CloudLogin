using System.Collections.Concurrent;
using System.Text.Json;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Core.Migration;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// In-memory implementations of the core repository contracts, faithful to the storage
/// semantics that matter: create-only inserts conflict, replaces are ETag-guarded, expiring
/// reads validate <c>ExpiresOn</c>, and session rotation is atomic per family.
/// </summary>
internal static class TestClone
{
    private static readonly JsonSerializerOptions Options = new();

    public static T Clone<T>(T source) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, Options), Options)!;
}

/// <summary>
/// The identity HMAC key the tests key their index with. A fixed value so hashes are stable
/// within a run, and a test-only one so nothing here resembles something a deployment might
/// paste into configuration.
/// </summary>
internal static class TestIdentityHmac
{
    /// <summary>32 bytes, base64 — the minimum a real deployment must supply.</summary>
    public const string Secret = "dGVzdC1vbmx5LWlkZW50aXR5LWhtYWMta2V5LTMyISE=";

    public static IdentityKeyHasher Hasher { get; } = IdentityKeyHasher.FromConfiguredSecret(Secret);
}

internal sealed class InMemoryUserRepository : IUserRepository
{
    public ConcurrentDictionary<string, UserDocument> Documents { get; } = new();

    public Task<UserDocument?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.TryGetValue(userId.ToString(), out UserDocument? user) ? TestClone.Clone(user) : null);

    public Task CreateAsync(UserDocument user, CancellationToken cancellationToken = default)
    {
        UserDocument copy = TestClone.Clone(user);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd(copy.Id, copy))
            throw new CoreConflictException("User id exists.");

        user.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(UserDocument user, CancellationToken cancellationToken = default)
    {
        lock (Documents)
        {
            if (!Documents.TryGetValue(user.Id, out UserDocument? current))
                throw new CoreConcurrencyException("User missing.");

            if (!string.Equals(current.ETag, user.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("ETag mismatch.");

            UserDocument copy = TestClone.Clone(user);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[user.Id] = copy;
            user.ETag = copy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Documents.TryRemove(userId.ToString(), out _);
        return Task.CompletedTask;
    }

    public Task UpdateLastSignedInAsync(Guid userId, DateTimeOffset lastSignedIn, CancellationToken cancellationToken = default)
    {
        if (Documents.TryGetValue(userId.ToString(), out UserDocument? user))
            user.LastSignedInOn = lastSignedIn;
        return Task.CompletedTask;
    }

    public Task<List<UserDocument>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values.Select(TestClone.Clone).ToList());

    public Task<List<UserDocument>> GetByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values
            .Where(user => string.Equals(user.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            .Select(TestClone.Clone).ToList());

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Documents.Count);
}

internal sealed class InMemoryCredentialRepository : ICredentialRepository
{
    public ConcurrentDictionary<(string UserId, string Id), CredentialDocument> Documents { get; } = new();

    public Task<CredentialDocument?> GetAsync(Guid userId, string credentialId, CancellationToken cancellationToken = default)
    {
        Documents.TryGetValue((userId.ToString(), credentialId), out CredentialDocument? credential);
        return Task.FromResult(credential is null || DocumentExpiry.IsExpired(credential) ? null : TestClone.Clone(credential));
    }

    public Task<List<CredentialDocument>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values
            .Where(credential => credential.UserId == userId.ToString() && !DocumentExpiry.IsExpired(credential))
            .Select(TestClone.Clone).ToList());

    public async Task<List<CredentialDocument>> GetByKindAsync(Guid userId, CredentialKinds kind, CancellationToken cancellationToken = default) =>
        [.. (await GetAllForUserAsync(userId, cancellationToken)).Where(credential => credential.Kind == kind)];

    public Task CreateAsync(CredentialDocument credential, CancellationToken cancellationToken = default)
    {
        DocumentExpiry.Recompute(credential);

        CredentialDocument copy = TestClone.Clone(credential);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd((credential.UserId, credential.Id), copy))
            throw new CoreConflictException("Credential exists.");

        credential.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Conditional whenever the incoming document carries an ETag, matching the Cosmos
    /// repository: a read-modify-write that lost a race must fail rather than overwrite.
    /// </summary>
    public Task UpsertAsync(CredentialDocument credential, CancellationToken cancellationToken = default)
    {
        DocumentExpiry.Recompute(credential);

        lock (Documents)
        {
            if (!string.IsNullOrEmpty(credential.ETag)
                && Documents.TryGetValue((credential.UserId, credential.Id), out CredentialDocument? current)
                && !string.Equals(current.ETag, credential.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("The credential changed since it was read.");

            CredentialDocument copy = TestClone.Clone(credential);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[(credential.UserId, credential.Id)] = copy;
            credential.ETag = copy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid userId, string credentialId, CancellationToken cancellationToken = default)
    {
        Documents.TryRemove((userId.ToString(), credentialId), out _);
        return Task.CompletedTask;
    }

    public Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        foreach ((string UserId, string Id) key in Documents.Keys.Where(key => key.UserId == userId.ToString()).ToList())
            Documents.TryRemove(key, out _);

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
{
    public ConcurrentDictionary<string, WorkspaceDocument> Documents { get; } = new();

    public Task<WorkspaceDocument?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.TryGetValue(workspaceId.ToString(), out WorkspaceDocument? workspace) ? TestClone.Clone(workspace) : null);

    public Task CreateAsync(WorkspaceDocument workspace, CancellationToken cancellationToken = default)
    {
        WorkspaceDocument copy = TestClone.Clone(workspace);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd(copy.Id, copy))
            throw new CoreConflictException("Workspace exists.");

        workspace.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(WorkspaceDocument workspace, CancellationToken cancellationToken = default)
    {
        lock (Documents)
        {
            if (!Documents.TryGetValue(workspace.Id, out WorkspaceDocument? current) ||
                !string.Equals(current.ETag, workspace.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("ETag mismatch.");

            WorkspaceDocument copy = TestClone.Clone(workspace);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[workspace.Id] = copy;
            workspace.ETag = copy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Documents.TryRemove(workspaceId.ToString(), out _);
        return Task.CompletedTask;
    }

    public Task<List<WorkspaceDocument>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values.Select(TestClone.Clone).ToList());
}

internal sealed class InMemoryWorkspaceAccessRepository : IWorkspaceAccessRepository
{
    public ConcurrentDictionary<(string WorkspaceId, string Id), WorkspaceAccessDocument> Documents { get; } = new();

    public Task<WorkspaceAccessDocument?> GetAsync(Guid workspaceId, string accessId, CancellationToken cancellationToken = default)
    {
        Documents.TryGetValue((workspaceId.ToString(), accessId), out WorkspaceAccessDocument? access);
        return Task.FromResult(access is null || DocumentExpiry.IsExpired(access) ? null : TestClone.Clone(access));
    }

    public Task<List<WorkspaceAccessDocument>> GetAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values
            .Where(access => access.WorkspaceId == workspaceId.ToString() && !DocumentExpiry.IsExpired(access))
            .Select(TestClone.Clone).ToList());

    public Task CreateAsync(WorkspaceAccessDocument access, CancellationToken cancellationToken = default)
    {
        DocumentExpiry.Recompute(access);
        WorkspaceAccessDocument copy = TestClone.Clone(access);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd((copy.WorkspaceId, copy.Id), copy))
            throw new CoreConflictException("Access record exists.");

        access.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(WorkspaceAccessDocument access, CancellationToken cancellationToken = default)
    {
        lock (Documents)
        {
            if (!Documents.TryGetValue((access.WorkspaceId, access.Id), out WorkspaceAccessDocument? current) ||
                !string.Equals(current.ETag, access.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("ETag mismatch.");

            DocumentExpiry.Recompute(access);
            WorkspaceAccessDocument copy = TestClone.Clone(access);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[(access.WorkspaceId, access.Id)] = copy;
            access.ETag = copy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid workspaceId, string accessId, CancellationToken cancellationToken = default)
    {
        Documents.TryRemove((workspaceId.ToString(), accessId), out _);
        return Task.CompletedTask;
    }

    public Task DeleteAllForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        foreach ((string WorkspaceId, string Id) key in Documents.Keys.Where(key => key.WorkspaceId == workspaceId.ToString()).ToList())
            Documents.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    public Task<List<WorkspaceAccessDocument>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values
            .Where(access => access.Kind == WorkspaceAccessKinds.Membership && access.UserId == userId.ToString())
            .Select(TestClone.Clone).ToList());
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
    public ConcurrentDictionary<(string FamilyId, string Id), object> Documents { get; } = new();
    private readonly object _batchLock = new();

    public Task<SessionFamilyDocument?> GetFamilyAsync(string familyId, CancellationToken cancellationToken = default)
    {
        Documents.TryGetValue((familyId, familyId), out object? document);
        return Task.FromResult(document is SessionFamilyDocument family ? TestClone.Clone(family) : null);
    }

    public Task<SessionTokenDocument?> GetTokenAsync(string familyId, string tokenId, CancellationToken cancellationToken = default)
    {
        Documents.TryGetValue((familyId, tokenId), out object? document);
        return Task.FromResult(document is SessionTokenDocument token ? TestClone.Clone(token) : null);
    }

    public Task CreateFamilyAsync(SessionFamilyDocument family, SessionTokenDocument firstToken, CancellationToken cancellationToken = default)
    {
        lock (_batchLock)
        {
            SessionFamilyDocument familyCopy = TestClone.Clone(family);
            familyCopy.ETag = Guid.NewGuid().ToString();
            SessionTokenDocument tokenCopy = TestClone.Clone(firstToken);
            tokenCopy.ETag = Guid.NewGuid().ToString();

            if (!Documents.TryAdd((family.FamilyId, family.Id), familyCopy))
                throw new CoreConflictException("Family exists.");

            Documents[(firstToken.FamilyId, firstToken.Id)] = tokenCopy;
            family.ETag = familyCopy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task RotateAsync(SessionFamilyDocument family, SessionTokenDocument consumedToken, SessionTokenDocument newToken, CancellationToken cancellationToken = default)
    {
        lock (_batchLock)
        {
            // Emulates the transactional batch: all preconditions checked, then all writes.
            if (!Documents.TryGetValue((family.FamilyId, family.Id), out object? familyStored) ||
                familyStored is not SessionFamilyDocument currentFamily ||
                !string.Equals(currentFamily.ETag, family.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("Family ETag mismatch.");

            if (!Documents.TryGetValue((consumedToken.FamilyId, consumedToken.Id), out object? tokenStored) ||
                tokenStored is not SessionTokenDocument currentToken ||
                !string.Equals(currentToken.ETag, consumedToken.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("Token ETag mismatch.");

            if (Documents.ContainsKey((newToken.FamilyId, newToken.Id)))
                throw new CoreConcurrencyException("New token already exists.");

            SessionFamilyDocument familyCopy = TestClone.Clone(family);
            familyCopy.ETag = Guid.NewGuid().ToString();
            SessionTokenDocument consumedCopy = TestClone.Clone(consumedToken);
            consumedCopy.ETag = Guid.NewGuid().ToString();
            SessionTokenDocument newCopy = TestClone.Clone(newToken);
            newCopy.ETag = Guid.NewGuid().ToString();

            Documents[(family.FamilyId, family.Id)] = familyCopy;
            Documents[(consumedToken.FamilyId, consumedToken.Id)] = consumedCopy;
            Documents[(newToken.FamilyId, newToken.Id)] = newCopy;
            family.ETag = familyCopy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task ReplaceFamilyAsync(SessionFamilyDocument family, CancellationToken cancellationToken = default)
    {
        lock (_batchLock)
        {
            if (!Documents.TryGetValue((family.FamilyId, family.Id), out object? stored) ||
                stored is not SessionFamilyDocument current ||
                !string.Equals(current.ETag, family.ETag, StringComparison.Ordinal))
                throw new CoreConcurrencyException("Family ETag mismatch.");

            SessionFamilyDocument copy = TestClone.Clone(family);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[(family.FamilyId, family.Id)] = copy;
            family.ETag = copy.ETag;
        }

        return Task.CompletedTask;
    }

    public Task<List<SessionFamilyDocument>> GetFamiliesForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values.OfType<SessionFamilyDocument>()
            .Where(family => family.UserId == userId.ToString() && !DocumentExpiry.IsExpired(family))
            .Select(TestClone.Clone).ToList());

    public Task<SessionTokenDocument?> FindTokenByIdAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        SessionTokenDocument? token = Documents.Values.OfType<SessionTokenDocument>()
            .FirstOrDefault(candidate => candidate.Id == tokenId);
        return Task.FromResult(token is null ? null : TestClone.Clone(token));
    }

    public Task<List<SessionFamilyDocument>> FindFamiliesBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.Values.OfType<SessionFamilyDocument>()
            .Where(family => family.SessionId == sessionId)
            .Select(TestClone.Clone).ToList());

    public Task UpsertTokenAsync(SessionTokenDocument token, CancellationToken cancellationToken = default)
    {
        lock (_batchLock)
        {
            DocumentExpiry.Recompute(token);
            SessionTokenDocument copy = TestClone.Clone(token);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[(token.FamilyId, token.Id)] = copy;
            token.ETag = copy.ETag;
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryLoginRequestRepository : ILoginRequestRepository
{
    public ConcurrentDictionary<string, LoginRequestDocument> Documents { get; } = new();
    private readonly object _lock = new();

    public Task<LoginRequestDocument?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        Documents.TryGetValue(requestId, out LoginRequestDocument? request);
        return Task.FromResult(request is null ? null : TestClone.Clone(request));
    }

    public Task CreateAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        DocumentExpiry.Recompute(request);
        LoginRequestDocument copy = TestClone.Clone(request);
        copy.ETag = Guid.NewGuid().ToString();

        if (!Documents.TryAdd(copy.Id, copy))
            throw new CoreConflictException("Request exists.");

        request.ETag = copy.ETag;
        return Task.CompletedTask;
    }

    public Task<bool> TryReplaceAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!Documents.TryGetValue(request.Id, out LoginRequestDocument? current) ||
                !string.Equals(current.ETag, request.ETag, StringComparison.Ordinal))
                return Task.FromResult(false);

            DocumentExpiry.Recompute(request);
            LoginRequestDocument copy = TestClone.Clone(request);
            copy.ETag = Guid.NewGuid().ToString();
            Documents[request.Id] = copy;
            request.ETag = copy.ETag;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryDeleteAsync(LoginRequestDocument request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!Documents.TryGetValue(request.Id, out LoginRequestDocument? current) ||
                !string.Equals(current.ETag, request.ETag, StringComparison.Ordinal))
                return Task.FromResult(false);

            Documents.TryRemove(request.Id, out _);
            return Task.FromResult(true);
        }
    }

    public Task<LoginRequestDocument?> FindByUserCodeHashAsync(string userCodeHash, CancellationToken cancellationToken = default)
    {
        LoginRequestDocument? match = Documents.Values.FirstOrDefault(request =>
            string.Equals(request.UserCodeHash, userCodeHash, StringComparison.Ordinal) &&
            !DocumentExpiry.IsExpired(request));

        return Task.FromResult(match is null ? null : TestClone.Clone(match));
    }
}

internal sealed class InMemoryAuditEventRepository : IAuditEventRepository
{
    public List<AuditEventDocument> Events { get; } = [];

    public Task AppendAsync(AuditEventDocument auditEvent, CancellationToken cancellationToken = default)
    {
        lock (Events)
            Events.Add(TestClone.Clone(auditEvent));

        return Task.CompletedTask;
    }

    public Task<List<AuditEventDocument>> GetPartitionAsync(string partitionKey, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        lock (Events)
            return Task.FromResult(Events
                .Where(auditEvent => auditEvent.PartitionKey == partitionKey)
                .OrderByDescending(auditEvent => auditEvent.OccurredOn)
                .Take(maxCount).ToList());
    }
}

/// <summary>
/// The identity index in memory, keyed exactly the way the Table Storage store keys it: the same
/// <see cref="IdentityKeyHasher"/>, the same realm-scoped table name, the same partition and row
/// keys. Tests therefore exercise the real HMAC path rather than a lookup by plaintext, which is
/// the one thing a fake here could get wrong without any test noticing.
/// </summary>
internal sealed class InMemoryIdentityKeyStore(IdentityKeyHasher hasher) : IIdentityKeyStore
{
    private readonly IdentityKeyHasher _hasher = hasher;
    private readonly object _lock = new();

    public ConcurrentDictionary<(string Table, string Partition, string Row), IdentityKey> Keys { get; } = new();
    public ConcurrentDictionary<(string Table, string Slot), Guid> Bootstraps { get; } = new();

    private IReadOnlyList<(string Table, string Partition, string Row)> LocationsOf(
        string realm, string canonicalValue) =>
        [.. _hasher.ComputeCandidateHashes(canonicalValue).Select(hash => (
            CloudLoginCoreContainers.IdentityKeysTableFor(realm),
            IdentityKey.PartitionKeyFor(
                IdentityKey.TypeOf(canonicalValue), IdentityKeyHasher.CurrentHashVersion, hash),
            hash))];

    public Task<IdentityKey?> ResolveAsync(string realm, string canonicalValue, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<(string Table, string Partition, string Row)> locations =
                LocationsOf(realm, canonicalValue);
            List<((string Table, string Partition, string Row) Location, IdentityKey Key)> found =
                [.. locations
                    .Select(location => (Location: location,
                        Key: Keys.TryGetValue(location, out IdentityKey? key) ? key : null))
                    .Where(match => match.Key is not null)
                    .Select(match => (match.Location, match.Key!))];

            if (found.Count == 0)
                return Task.FromResult<IdentityKey?>(null);

            Guid owner = found[0].Key.UserId;

            if (found.Any(match => match.Key.UserId != owner))
                throw new CoreConflictException("Identity resolves to different users under configured HMAC keys.");

            (string Table, string Partition, string Row) primary = locations[0];

            if (!Keys.TryGetValue(primary, out IdentityKey? primaryKey))
            {
                IdentityKey source = found[0].Key;
                primaryKey = new IdentityKey
                {
                    Type = source.Type,
                    Hash = primary.Row,
                    UserId = source.UserId,
                    ContactId = source.ContactId,
                    CreatedOn = source.CreatedOn,
                    SchemaVersion = source.SchemaVersion,
                    HashVersion = source.HashVersion,
                    NormalizationVersion = source.NormalizationVersion
                };
                Keys[primary] = primaryKey;
            }

            foreach (((string Table, string Partition, string Row) location, _) in found)
                if (location != primary)
                    Keys.TryRemove(location, out _);

            return Task.FromResult<IdentityKey?>(primaryKey);
        }
    }

    public Task InsertAsync(string realm, IdentityKeyClaim claim, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<(string Table, string Partition, string Row)> locations =
                LocationsOf(realm, claim.CanonicalValue);
            (string Table, string Partition, string Row) location = locations[0];

            if (locations.Any(Keys.ContainsKey))
                throw new CoreConflictException("Identity claimed.");

            IdentityKey key = new()
            {
                Type = claim.Type,
                Hash = location.Row,
                UserId = claim.UserId,
                ContactId = claim.ContactId,
                CreatedOn = DateTimeOffset.UtcNow
            };

            // Create-only, matching the Table store: a claimed identity conflicts rather than
            // being overwritten.
            if (!Keys.TryAdd(location, key))
                throw new CoreConflictException("Identity claimed.");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string realm, string canonicalValue, CancellationToken cancellationToken = default)
    {
        foreach ((string Table, string Partition, string Row) location in LocationsOf(realm, canonicalValue))
            Keys.TryRemove(location, out _);

        return Task.CompletedTask;
    }

    public Task<bool> TryReserveBootstrapAsync(string realm, string slotName, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Bootstraps.TryAdd((CloudLoginCoreContainers.IdentityKeysTableFor(realm), slotName), userId));
}

internal sealed class InMemoryUserWorkspaceIndexStore : IUserWorkspaceIndexStore
{
    public ConcurrentDictionary<(string Realm, Guid UserId, Guid WorkspaceId), DateTimeOffset> Entries { get; } = new();

    public Task<List<Guid>> GetWorkspaceIdsAsync(string realm, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.Keys
            .Where(key => key.Realm == realm && key.UserId == userId)
            .Select(key => key.WorkspaceId).ToList());

    public Task UpsertAsync(string realm, Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Entries[(realm, userId, workspaceId)] = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string realm, Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        Entries.TryRemove((realm, userId, workspaceId), out _);
        return Task.CompletedTask;
    }
}

internal sealed class ListLegacyUserSource(List<CloudUser> users) : ILegacyUserSource
{
    private readonly List<CloudUser> _users = users;

    public async IAsyncEnumerable<CloudUser> EnumerateUsersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (CloudUser user in _users)
        {
            await Task.Yield();
            yield return user;
        }
    }

    public Task<int> CountUsersAsync(CancellationToken cancellationToken = default) => Task.FromResult(_users.Count);
}

internal sealed class InMemoryMigrationCheckpointStore : IMigrationCheckpointStore
{
    public MigrationCheckpoint? Stored { get; set; }
    public List<MigrationReport> Reports { get; } = [];
    public int SaveCount { get; private set; }

    public Task<MigrationCheckpoint?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored is null ? null : TestClone.Clone(Stored));

    public Task SaveAsync(MigrationCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        Stored = TestClone.Clone(checkpoint);
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task SaveReportAsync(MigrationReport report, CancellationToken cancellationToken = default)
    {
        Reports.Add(report);
        return Task.CompletedTask;
    }
}
