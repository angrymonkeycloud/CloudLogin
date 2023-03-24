using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq.Expressions;

namespace AngryMonkey.CloudLogin;
public class CosmosMethods
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
        IQueryable<User> usersQueryable = Queryable<User>("User", Container, user => user.Inputs.Where(key => key.Format == InputFormat.EmailAddress && key.Input.Equals(emailAddress.Trim(), StringComparison.OrdinalIgnoreCase)).Any());

        var users = await ToListAsync(usersQueryable);

        return users.FirstOrDefault();
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

        IQueryable<User> usersQueryable = Queryable<User>("User", Container, user
            => user.Inputs.Any(key => key.Format == InputFormat.PhoneNumber &&
            key.Input.Equals(phoneNumber.Number)
                && (string.IsNullOrEmpty(phoneNumber.CountryCode)
                || key.PhoneNumberCountryCode.Equals(phoneNumber.CountryCode, StringComparison.OrdinalIgnoreCase))));

        var users = await ToListAsync(usersQueryable);

        return users.FirstOrDefault();
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        CloudRequest request = new() { ID = requestId };

        ItemResponse<CloudRequest> response = await Container.ReadItemAsync<CloudRequest>(GetCosmosId(request), GetPartitionKey(request));

        await Container.DeleteItemAsync<CloudRequest>(GetCosmosId(request), GetPartitionKey(request));

        CloudRequest selectedRequest = response.Resource;

        if (selectedRequest.UserId == null) return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        IQueryable<User> usersQueryable = Queryable<User>("User", Container).Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        var users = await ToListAsync(usersQueryable);

        return users;
    }
    public async Task<User> GetUserById(Guid id)
    {
        User user = new() { ID = id };
        ItemResponse<User> response = await Container.ReadItemAsync<User>(GetCosmosId(user), GetPartitionKey(user));

        return response.Resource;
    }

    public async Task<List<User>> GetUsers()
    {
        IQueryable<User> usersQueryable = Queryable<User>("User", Container);

        return await ToListAsync(usersQueryable);
    }

    public async Task AddInput(Guid userId, LoginInput Input)
    {
        User user = await GetUserById(userId);

        user.Inputs.Add(Input);

        await Container.UpsertItemAsync(user);
    }
    public async Task DeleteUser(Guid userId)
    {
        User user = new() { ID = userId };
        await Container.DeleteItemStreamAsync(GetCosmosId(user), GetPartitionKey(user));
    }
}