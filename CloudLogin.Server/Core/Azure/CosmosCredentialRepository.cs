using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Core.Azure;

public sealed class CosmosCredentialRepository(CosmosCoreDatabase database) : ICredentialRepository
{
    private readonly CosmosCoreDatabase _database = database;

    private Task<Container> ContainerAsync(CancellationToken cancellationToken) =>
        _database.GetContainerAsync(CloudLoginCoreContainers.Credentials, cancellationToken);

    public async Task<CredentialDocument?> GetAsync(Guid userId, string credentialId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        CredentialDocument? credential = await CosmosCoreOperations.ReadOrNullAsync<CredentialDocument>(
            container, credentialId, new PartitionKey(userId.ToString()), cancellationToken);

        // Cosmos deletes expired documents asynchronously; never hand one out.
        return credential is not null && DocumentExpiry.IsExpired(credential) ? null : credential;
    }

    public async Task<List<CredentialDocument>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        QueryDefinition query = new("SELECT * FROM c");
        QueryRequestOptions options = new() { PartitionKey = new PartitionKey(userId.ToString()) };

        List<CredentialDocument> credentials = await CosmosCoreOperations.QueryAsync<CredentialDocument>(container, query, options, cancellationToken);
        return [.. credentials.Where(credential => !DocumentExpiry.IsExpired(credential))];
    }

    public async Task<List<CredentialDocument>> GetByKindAsync(Guid userId, CredentialKinds kind, CancellationToken cancellationToken = default)
    {
        List<CredentialDocument> all = await GetAllForUserAsync(userId, cancellationToken);
        return [.. all.Where(credential => credential.Kind == kind)];
    }

    public async Task CreateAsync(CredentialDocument credential, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(credential);
        await CosmosCoreOperations.CreateOnlyAsync(container, credential, new PartitionKey(credential.UserId), cancellationToken);
    }

    /// <summary>
    /// Upsert for rotations. Conditional whenever the document carries an ETag — that is, whenever
    /// it came from a read — so a read-modify-write cannot silently lose a concurrent change:
    /// two password changes racing, or a passkey sign-count update overwriting a rename. A freshly
    /// constructed document has no ETag and writes unconditionally, which is the create case.
    /// </summary>
    public async Task UpsertAsync(CredentialDocument credential, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        DocumentExpiry.Recompute(credential);

        ItemRequestOptions? options = string.IsNullOrEmpty(credential.ETag)
            ? null
            : new ItemRequestOptions { IfMatchEtag = credential.ETag };

        try
        {
            await container.UpsertItemAsync(
                credential, new PartitionKey(credential.UserId), options, cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new CoreConcurrencyException("The credential changed since it was read.");
        }
    }

    public async Task DeleteAsync(Guid userId, string credentialId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        await CosmosCoreOperations.DeleteIfExistsAsync<CredentialDocument>(container, credentialId, new PartitionKey(userId.ToString()), cancellationToken);
    }

    public async Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Container container = await ContainerAsync(cancellationToken);
        List<CredentialDocument> all = await GetAllForUserAsync(userId, cancellationToken);

        foreach (CredentialDocument credential in all)
            await CosmosCoreOperations.DeleteIfExistsAsync<CredentialDocument>(container, credential.Id, new PartitionKey(credential.UserId), cancellationToken);
    }
}
