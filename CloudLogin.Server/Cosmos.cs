using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq.Expressions;

namespace AngryMonkey.CloudLogin;
public class CosmosMethods : DataParse
{
    #region Internal

    internal IQueryable<T> Queryable<T>(string partitionKey, Container selectedContainer) where T : BaseRecord
    {
        return Queryable<T>(partitionKey, selectedContainer, null);
    }

    internal static PartitionKey GetPartitionKey<T>(string partitionKey)
    {
        if (!string.IsNullOrEmpty(partitionKey))
            return new PartitionKey(partitionKey);

        return new PartitionKey(typeof(T).Name);
    }

    internal IQueryable<T> Queryable<T>(string partitionKey, Container selectedContainer, Expression<Func<T, bool>>? predicate) where T : BaseRecord
    {
        var container = selectedContainer.GetItemLinqQueryable<T>(requestOptions: new QueryRequestOptions())
                                 .Where(key => key.Discriminator == typeof(T).Name);

        if (!string.IsNullOrEmpty(partitionKey))
            container = container.Where(key => key.PartitionKey == partitionKey);

        if (predicate != null)
            container = container.Where(predicate);

        return container;
    }

    internal async Task<List<T>> ToListAsync<T>(IQueryable<T> query) where T : BaseRecord
    {
        try
        {
            List<T> list = new();

            using FeedIterator<T> setIterator = query.ToFeedIterator();

            while (setIterator.HasMoreResults)
                foreach (var item in await setIterator.ReadNextAsync())
                    list.Add(item);
            return list;
        }
        catch (Exception e)
        {
            throw;
        }
    }

    internal static PartitionKey GetPartitionKey<T>(T record) where T : BaseRecord => new PartitionKey(record.PartitionKey);

    internal static string GetCosmosId<T>(T record) where T : BaseRecord => $"{record.Discriminator}|{record.ID}";

    #endregion

    public CosmosMethods(string connectionString, string databaseId, string containerId)
    {
        CosmosClient client = new(connectionString, new CosmosClientOptions() { SerializerOptions = new() { IgnoreNullValues = true } });

        Container = client.GetContainer(databaseId, containerId);

    }

    public Container Container { get; set; }

    public async Task<User?> GetUserByEmailAddress(string emailAddress)
    {
        IQueryable<DbUser> usersQueryable = Queryable<DbUser>("User", Container, user => user.Inputs.Where(key => key.Format == InputFormat.EmailAddress && key.Input.Equals(emailAddress.Trim(), StringComparison.OrdinalIgnoreCase)).Any());

        var users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByInput(string input)
    {
        var user = await GetUserByEmailAddress(input);
        if (user == null)
        {
            CloudGeographyClient geographyClient = new();

            return await GetUserByPhoneNumber(geographyClient.PhoneNumbers.Get(input));
        }
        return user;
    }

    public async Task<User?> GetUserByPhoneNumber(string number)
    {
        CloudGeographyClient geographyClient = new();

        return await GetUserByPhoneNumber(geographyClient.PhoneNumbers.Get(number));
    }

    public async Task<User?> GetUserByPhoneNumber(PhoneNumber phoneNumber)
    {
        CloudGeographyClient cloudGeography = new();

        IQueryable<DbUser> usersQueryable = Queryable<DbUser>("User", Container, user
            => user.Inputs.Any(key => key.Format == InputFormat.PhoneNumber &&
            key.Input.Equals(phoneNumber.Number)
                && (string.IsNullOrEmpty(phoneNumber.CountryCode)
                || key.PhoneNumberCountryCode.Equals(phoneNumber.CountryCode, StringComparison.OrdinalIgnoreCase))));

        var users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        DbRequest request = new() { ID = requestId };

        ItemResponse<DbRequest> response = await Container.ReadItemAsync<DbRequest>(GetCosmosId(request), GetPartitionKey(request));

        await Container.DeleteItemAsync<DbRequest>(GetCosmosId(request), GetPartitionKey(request));

        DbRequest selectedRequest = response.Resource;

        if (selectedRequest.UserId == null) return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        IQueryable<DbUser> usersQueryable = Queryable<DbUser>("User", Container).Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        var users = await ToListAsync(usersQueryable);

        return Parse(users);
    }
    public async Task<User> GetUserById(Guid id)
    {
        DbUser user = new() { ID = id };
        ItemResponse<DbUser> response = await Container.ReadItemAsync<DbUser>(GetCosmosId(user), GetPartitionKey(user));

        return Parse(response.Resource);
    }

    public async Task<List<User>> GetUsers()
    {
        IQueryable<DbUser> usersQueryable = Queryable<DbUser>("User", Container);

        return Parse(await ToListAsync(usersQueryable));
    }

    public async Task CreateRequest(Guid userId, Guid requestId)
    {
        DbRequest request = new()
        {
            ID = requestId,
            UserId = userId
        };

        await Container.CreateItemAsync(request);

    }
    public async Task Update(User user)
    {
        DbUser dbUser = Parse(user);
        await Container.UpsertItemAsync(dbUser);
    }
    public async Task Create(User user)
    {
        DbUser dbUser = Parse(user);
        await Container.CreateItemAsync(dbUser);
    }
    public async Task AddInput(Guid userId, LoginInput Input)
    {
        User user = await GetUserById(userId);

        user.Inputs.Add(Input);

        await Container.UpsertItemAsync(user);
    }
    public async Task DeleteUser(Guid userId)
    {
        DbUser user = new() { ID = userId };
        await Container.DeleteItemStreamAsync(GetCosmosId(user), GetPartitionKey(user));
    }
}