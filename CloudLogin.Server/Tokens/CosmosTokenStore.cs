using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Cosmos-backed <see cref="ICloudLoginTokenStore"/>. Shares the container the rest
/// of CloudLogin uses; records are separated by the partition key their
/// <see cref="CloudLoginBaseRecord"/> type declares.
/// </summary>
public sealed class CosmosTokenStore(Container container) : ICloudLoginTokenStore
{
    private readonly Container _container = container;

    public async Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(
        CancellationToken cancellationToken = default)
    {
        QueryDefinition query = new(
            $"SELECT * FROM root WHERE root[\"{CloudLoginBaseRecord.GetTypePropertyName()}\"] = @type");
        query.WithParameter("@type", "SigningKey");

        List<CloudLoginSigningKey> keys = [];
        using FeedIterator<CloudLoginSigningKey> iterator = _container.GetItemQueryIterator<CloudLoginSigningKey>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("SigningKey") });

        while (iterator.HasMoreResults)
            keys.AddRange(await iterator.ReadNextAsync(cancellationToken));

        return keys;
    }

    public async Task SaveSigningKeyAsync(
        CloudLoginSigningKey key,
        CancellationToken cancellationToken = default) =>
        await _container.UpsertItemAsync(
            key,
            new PartitionKey(key.PartitionKeyValue),
            cancellationToken: cancellationToken);

    public async Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        QueryDefinition query = new(
            $"SELECT * FROM root WHERE root[\"{CloudLoginBaseRecord.GetTypePropertyName()}\"] = @type AND root.TokenHash = @hash");
        query.WithParameter("@type", "RefreshToken");
        query.WithParameter("@hash", tokenHash);

        using FeedIterator<CloudLoginRefreshToken> iterator = _container.GetItemQueryIterator<CloudLoginRefreshToken>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey("RefreshToken"),
                MaxItemCount = 1
            });

        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudLoginRefreshToken> page = await iterator.ReadNextAsync(cancellationToken);
            CloudLoginRefreshToken? match = page.FirstOrDefault();

            if (match is not null)
                return match;
        }

        return null;
    }

    public async Task SaveRefreshTokenAsync(
        CloudLoginRefreshToken token,
        CancellationToken cancellationToken = default) =>
        await _container.UpsertItemAsync(
            token,
            new PartitionKey(token.PartitionKeyValue),
            cancellationToken: cancellationToken);

    public Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default) =>
        RevokeWhereAsync("root.FamilyId = @value", familyId, cancellationToken);

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        RevokeWhereAsync("root.SessionId = @value", sessionId, cancellationToken);

    public Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        RevokeWhereAsync("root.UserId = @value", userId.ToString(), cancellationToken);

    private async Task RevokeWhereAsync(
        string predicate,
        string value,
        CancellationToken cancellationToken)
    {
        QueryDefinition query = new(
            $"SELECT * FROM root WHERE root[\"{CloudLoginBaseRecord.GetTypePropertyName()}\"] = @type AND {predicate} AND root.IsRevoked = false");
        query.WithParameter("@type", "RefreshToken");
        query.WithParameter("@value", value);

        using FeedIterator<CloudLoginRefreshToken> iterator = _container.GetItemQueryIterator<CloudLoginRefreshToken>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("RefreshToken") });

        while (iterator.HasMoreResults)
            foreach (CloudLoginRefreshToken token in await iterator.ReadNextAsync(cancellationToken))
            {
                token.IsRevoked = true;
                await SaveRefreshTokenAsync(token, cancellationToken);
            }
    }
}
