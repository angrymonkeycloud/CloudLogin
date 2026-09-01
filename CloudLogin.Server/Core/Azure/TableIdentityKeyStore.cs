using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Azure;
using Azure.Data.Tables;
using System.Collections.Concurrent;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

/// <summary>
/// The identity index over Azure Table Storage. Permanent point-lookup records only — nothing
/// expiring is ever written here. Inserts use <see cref="TableClient.AddEntityAsync{T}"/>
/// (create-only): a colliding claim surfaces as <see cref="CoreConflictException"/> instead of
/// silently overwriting another user's identity.
/// <para>
/// Row keys are HMACs, not digests, and the canonical value is never stored — see
/// <see cref="IdentityKeyHasher"/> and <see cref="IdentityKey"/>. Each realm gets its own table,
/// so identities cannot leak between realms sharing one storage account.
/// </para>
/// </summary>
public sealed class TableIdentityKeyStore(TableServiceClient tableService, IdentityKeyHasher hasher) : IIdentityKeyStore
{
    private readonly TableServiceClient _tableService = tableService;
    private readonly IdentityKeyHasher _hasher = hasher;
    private readonly ConcurrentDictionary<string, Task<TableClient>> _tables = new(StringComparer.OrdinalIgnoreCase);

    private const string BootstrapPartitionKey = "bootstrap";

    private Task<TableClient> TableAsync(string realm) =>
        _tables.GetOrAdd(CloudLoginCoreContainers.IdentityKeysTableFor(realm), CreateAsync);

    private async Task<TableClient> CreateAsync(string tableName)
    {
        TableClient table = _tableService.GetTableClient(tableName);
        await table.CreateIfNotExistsAsync();
        return table;
    }

    /// <summary>The primary location first, then each read-only fallback location.</summary>
    private IReadOnlyList<(string PartitionKey, string Hash)> LocationsOf(string canonicalValue) =>
        [.. _hasher.ComputeCandidateHashes(canonicalValue).Select(hash => (
            IdentityKey.PartitionKeyFor(
                IdentityKey.TypeOf(canonicalValue), IdentityKeyHasher.CurrentHashVersion, hash),
            hash))];

    private static async Task<TableEntity?> ReadOrNullAsync(
        TableClient table, string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            Response<TableEntity> response = await table.GetEntityAsync<TableEntity>(
                partitionKey, rowKey, cancellationToken: cancellationToken);

            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<IdentityKey?> ResolveAsync(string realm, string canonicalValue, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(realm);
        IReadOnlyList<(string PartitionKey, string Hash)> locations = LocationsOf(canonicalValue);
        List<(TableEntity Entity, (string PartitionKey, string Hash) Location)> found = [];

        foreach ((string partitionKey, string hash) in locations)
        {
            TableEntity? entity = await ReadOrNullAsync(table, partitionKey, hash, cancellationToken);

            if (entity is not null)
                found.Add((entity, (partitionKey, hash)));
        }

        if (found.Count == 0)
            return null;

        Guid owner = found[0].Entity.GetGuid("UserId") ?? Guid.Empty;

        if (found.Any(match => match.Entity.GetGuid("UserId") != owner))
            throw new CoreConflictException(
                "The identity resolves to different users under configured HMAC keys. Resolve the conflicting claims before sign-in.");

        (string PartitionKey, string Hash) primary = locations[0];
        TableEntity? primaryEntity = found
            .Where(match => match.Location == primary)
            .Select(match => match.Entity)
            .FirstOrDefault();

        await TryRekeyAsync(table, primary, found, cancellationToken);

        return FromEntity(primaryEntity ?? found[0].Entity);
    }

    public async Task InsertAsync(string realm, IdentityKeyClaim claim, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(realm);
        IReadOnlyList<(string PartitionKey, string Hash)> locations = LocationsOf(claim.CanonicalValue);
        (string partitionKey, string hash) = locations[0];

        // A row written with an old key still owns the identity. Checking every fallback before
        // creating the primary row prevents a rotation from making the same identity claimable by
        // a second user.
        foreach ((string existingPartition, string existingHash) in locations)
            if (await ReadOrNullAsync(table, existingPartition, existingHash, cancellationToken) is not null)
                throw new CoreConflictException("The identity is already claimed.");

        TableEntity entity = CreateEntity(
            partitionKey, hash, claim.UserId, claim.ContactId, claim.Type.ToString(), DateTimeOffset.UtcNow);

        try
        {
            // Create-only: an identity already claimed - by anyone - surfaces as a conflict rather
            // than overwriting the row that holds it.
            await table.AddEntityAsync(entity, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            throw new CoreConflictException("The identity is already claimed.");
        }
    }

    public async Task DeleteAsync(string realm, string canonicalValue, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(realm);
        foreach ((string partitionKey, string hash) in LocationsOf(canonicalValue))
        {
            try
            {
                await table.DeleteEntityAsync(partitionKey, hash, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
            }
        }
    }

    public async Task<bool> DeleteIfOwnedAsync(
        string realm, string canonicalValue, Guid expectedUserId,
        CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(realm);
        bool removed = false;

        foreach ((string partitionKey, string hash) in LocationsOf(canonicalValue))
        {
            TableEntity? entity = await ReadOrNullAsync(table, partitionKey, hash, cancellationToken);

            if (entity is null || entity.GetGuid("UserId") != expectedUserId)
                continue;

            try
            {
                // Conditional on the ETag read above: a claim re-made by someone else in between
                // survives instead of being deleted on the strength of a stale read.
                await table.DeleteEntityAsync(partitionKey, hash, entity.ETag, cancellationToken);
                removed = true;
            }
            catch (RequestFailedException exception) when (exception.Status is 404 or 412)
            {
            }
        }

        return removed;
    }

    private static TableEntity CreateEntity(
        string partitionKey,
        string hash,
        Guid userId,
        Guid? contactId,
        string? identityType,
        DateTimeOffset createdOn) => new(partitionKey, hash)
        {
            ["UserId"] = userId,
            ["ContactId"] = contactId,
            ["IdentityType"] = identityType,
            ["SchemaVersion"] = CloudLoginCoreSchema.CurrentVersion,
            ["HashVersion"] = IdentityKeyHasher.CurrentHashVersion,
            ["NormalizationVersion"] = IdentityKeyHasher.CurrentNormalizationVersion,
            ["CreatedOn"] = createdOn
        };

    /// <summary>
    /// Creates/confirms the primary row before conditionally removing matching fallback rows.
    /// Migration is best-effort so a transient write failure never turns a successful fallback
    /// read into a user lockout. A conflicting owner remains fatal because choosing one would be
    /// an account-takeover risk.
    /// </summary>
    private static async Task TryRekeyAsync(
        TableClient table,
        (string PartitionKey, string Hash) primary,
        IReadOnlyList<(TableEntity Entity, (string PartitionKey, string Hash) Location)> found,
        CancellationToken cancellationToken)
    {
        TableEntity? primaryEntity = found
            .Where(match => match.Location == primary)
            .Select(match => match.Entity)
            .FirstOrDefault();
        TableEntity source = primaryEntity ?? found[0].Entity;

        try
        {
            if (primaryEntity is null)
            {
                TableEntity replacement = CreateEntity(
                    primary.PartitionKey,
                    primary.Hash,
                    source.GetGuid("UserId") ?? Guid.Empty,
                    source.GetGuid("ContactId"),
                    source.GetString("IdentityType"),
                    source.GetDateTimeOffset("CreatedOn") ?? DateTimeOffset.UtcNow);

                try
                {
                    await table.AddEntityAsync(replacement, cancellationToken);
                }
                catch (RequestFailedException exception) when (exception.Status == 409)
                {
                    TableEntity? existing =
                        await ReadOrNullAsync(table, primary.PartitionKey, primary.Hash, cancellationToken);

                    if (existing?.GetGuid("UserId") != source.GetGuid("UserId"))
                        throw new CoreConflictException(
                            "The identity became claimed by a different user while its HMAC key was being migrated.");
                }
            }

            foreach ((TableEntity entity, (string PartitionKey, string Hash) location) in found)
            {
                if (location == primary)
                    continue;

                try
                {
                    await table.DeleteEntityAsync(
                        location.PartitionKey, location.Hash, entity.ETag, cancellationToken);
                }
                catch (RequestFailedException exception) when (exception.Status is 404 or 412)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CoreConflictException)
        {
            throw;
        }
        catch (RequestFailedException)
        {
            // The fallback row remains authoritative and will be retried on the next lookup.
        }
    }

    public async Task<bool> TryReserveBootstrapAsync(string realm, string slotName, Guid userId, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(realm);

        TableEntity entity = new(BootstrapPartitionKey, slotName)
        {
            ["UserId"] = userId,
            ["SchemaVersion"] = CloudLoginCoreSchema.CurrentVersion,
            ["CreatedOn"] = DateTimeOffset.UtcNow
        };

        try
        {
            await table.AddEntityAsync(entity, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            return false;
        }
    }

    public async Task ReleaseBootstrapAsync(
        string realm, string slotName, Guid expectedUserId,
        CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(realm);

        try
        {
            Response<TableEntity> response = await table.GetEntityAsync<TableEntity>(
                BootstrapPartitionKey, slotName, cancellationToken: cancellationToken);

            if (response.Value.GetGuid("UserId") == expectedUserId)
                await table.DeleteEntityAsync(BootstrapPartitionKey, slotName, response.Value.ETag, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
        }
    }

    private static IdentityKey FromEntity(TableEntity entity) => new()
    {
        Type = Enum.TryParse(entity.GetString("IdentityType"), out IdentityKeyTypes type) ? type : IdentityKeyTypes.Email,
        Hash = entity.RowKey,
        UserId = entity.GetGuid("UserId") ?? Guid.Empty,
        ContactId = entity.GetGuid("ContactId"),
        CreatedOn = entity.GetDateTimeOffset("CreatedOn") ?? DateTimeOffset.MinValue,
        SchemaVersion = entity.GetInt32("SchemaVersion") ?? CloudLoginCoreSchema.CurrentVersion,
        HashVersion = entity.GetInt32("HashVersion") ?? IdentityKeyHasher.CurrentHashVersion,
        NormalizationVersion = entity.GetInt32("NormalizationVersion") ?? IdentityKeyHasher.CurrentNormalizationVersion
    };
}

/// <summary>
/// Optional user-to-workspace lookup acceleration. Idempotent upserts and deletes only, so an
/// outbox replay or reconciliation sweep can always repair it; readers must treat it as a hint
/// and confirm against the WorkspaceAccess container.
/// </summary>
public sealed class TableUserWorkspaceIndexStore(TableServiceClient tableService) : IUserWorkspaceIndexStore
{
    private readonly TableServiceClient _tableService = tableService;
    private TableClient? _table;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private async Task<TableClient> TableAsync(CancellationToken cancellationToken)
    {
        if (_table is not null)
            return _table;

        await _initLock.WaitAsync(cancellationToken);

        try
        {
            if (_table is null)
            {
                TableClient table = _tableService.GetTableClient(CloudLoginCoreContainers.UserWorkspaceIndexTable);
                await table.CreateIfNotExistsAsync(cancellationToken);
                _table = table;
            }

            return _table;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string PartitionFor(string realm, Guid userId) => $"{realm}-{userId}";

    public async Task<List<Guid>> GetWorkspaceIdsAsync(string realm, Guid userId, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(cancellationToken);
        List<Guid> workspaceIds = [];

        await foreach (TableEntity entity in table.QueryAsync<TableEntity>(
            entity => entity.PartitionKey == PartitionFor(realm, userId), cancellationToken: cancellationToken))
        {
            if (Guid.TryParse(entity.RowKey, out Guid workspaceId))
                workspaceIds.Add(workspaceId);
        }

        return workspaceIds;
    }

    public async Task UpsertAsync(string realm, Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(cancellationToken);
        TableEntity entity = new(PartitionFor(realm, userId), workspaceId.ToString())
        {
            ["SchemaVersion"] = CloudLoginCoreSchema.CurrentVersion,
            ["UpdatedOn"] = DateTimeOffset.UtcNow
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task DeleteAsync(string realm, Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        TableClient table = await TableAsync(cancellationToken);
        await table.DeleteEntityAsync(PartitionFor(realm, userId), workspaceId.ToString(), cancellationToken: cancellationToken);
    }
}
