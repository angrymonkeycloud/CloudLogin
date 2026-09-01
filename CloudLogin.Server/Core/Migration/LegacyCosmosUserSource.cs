using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Migration;

/// <summary>
/// Reads the legacy mixed container. Strictly read-only: this type has no write, delete, or
/// patch call anywhere — the legacy container survives the migration untouched for the rollback
/// window and is never deleted automatically.
/// </summary>
public sealed class LegacyCosmosUserSource(Container legacyContainer) : ILegacyUserSource
{
    private readonly Container _container = legacyContainer;

    public async IAsyncEnumerable<CloudUser> EnumerateUsersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");

        QueryDefinition query = new QueryDefinition(
                "SELECT VALUE root FROM root WHERE root[\"" + CloudLoginBaseRecord.GetTypePropertyName() + "\"] = @userType")
            .WithParameter("@userType", userType);

        using FeedIterator<CloudUserInfo> iterator = _container.GetItemQueryIterator<CloudUserInfo>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudUserInfo> page = await iterator.ReadNextAsync(cancellationToken);

            foreach (CloudUserInfo document in page)
            {
                CloudUser? user = DataParse.Parse(document);
                if (user is not null)
                    yield return user;
            }
        }
    }

    public async Task<int> CountUsersAsync(CancellationToken cancellationToken = default)
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");

        QueryDefinition query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM root WHERE root[\"" + CloudLoginBaseRecord.GetTypePropertyName() + "\"] = @userType")
            .WithParameter("@userType", userType);

        using FeedIterator<int> iterator = _container.GetItemQueryIterator<int>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        int count = 0;

        while (iterator.HasMoreResults)
            foreach (int value in await iterator.ReadNextAsync(cancellationToken))
                count += value;

        return count;
    }
}

/// <summary>
/// Blob-backed checkpoint and report storage under <c>migration/</c> in the CloudLogin storage
/// container. Non-expiring, non-queryable content — exactly what Blob Storage is for.
/// </summary>
public sealed class BlobMigrationCheckpointStore(AzureStorageConfiguration storage) : IMigrationCheckpointStore
{
    private readonly AzureStorageConfiguration _storage = storage;

    private const string CheckpointPath = "migration/checkpoint.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private async Task<BlobContainerClient> ContainerAsync()
    {
        BlobContainerClient container = _storage.CreateContainerClient();
        await container.CreateIfNotExistsAsync();
        return container;
    }

    public async Task<MigrationCheckpoint?> LoadAsync(CancellationToken cancellationToken = default)
    {
        BlobClient blob = (await ContainerAsync()).GetBlobClient(CheckpointPath);

        try
        {
            global::Azure.Response<BlobDownloadResult> response = await blob.DownloadContentAsync(cancellationToken);
            return JsonSerializer.Deserialize<MigrationCheckpoint>(response.Value.Content.ToString());
        }
        catch (global::Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task SaveAsync(MigrationCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        BlobClient blob = (await ContainerAsync()).GetBlobClient(CheckpointPath);
        BinaryData payload = new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(checkpoint, SerializerOptions)));

        await blob.UploadAsync(payload, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
        }, cancellationToken);
    }

    public async Task SaveReportAsync(MigrationReport report, CancellationToken cancellationToken = default)
    {
        string reportPath = $"migration/report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{(report.DryRun ? "-dryrun" : "")}.json";
        BlobClient blob = (await ContainerAsync()).GetBlobClient(reportPath);
        BinaryData payload = new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, SerializerOptions)));

        await blob.UploadAsync(payload, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
        }, cancellationToken);
    }
}
