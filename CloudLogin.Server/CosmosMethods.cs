using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using static System.Formats.Asn1.AsnWriter;

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

    public CosmosMethods(CosmosConfiguration cosmosConfiguration)
    {
        CosmosClient client = new(cosmosConfiguration.ConnectionString, new CosmosClientOptions() { SerializerOptions = new() { IgnoreNullValues = true } });

        Container = client.GetContainer(cosmosConfiguration.DatabaseId, cosmosConfiguration.ContainerId);
    }

    public Container Container { get; set; }

    public async Task<User?> GetUserByEmailAddress(string emailAddress)
    {
        IQueryable<Data.User> usersQueryable = Queryable<Data.User>("User", Container, user => user.Inputs.Any(key => key.Format == InputFormat.EmailAddress && key.Input.Equals(emailAddress.Trim(), StringComparison.OrdinalIgnoreCase)));

        List<Data.User> users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByInput(string input)
    {
        User? user = await GetUserByEmailAddress(input);
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

        IQueryable<Data.User> usersQueryable = Queryable<Data.User>("User", Container, user
            => user.Inputs.Any(key => key.Format == InputFormat.PhoneNumber &&
            key.Input.Equals(phoneNumber.Number)
                && (string.IsNullOrEmpty(phoneNumber.CountryCode)
                || key.PhoneNumberCountryCode.Equals(phoneNumber.CountryCode, StringComparison.OrdinalIgnoreCase))));

        List<Data.User> users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        Data.Request request = new() { ID = requestId };

        ItemResponse<Data.Request> response = await Container.ReadItemAsync<Data.Request>(GetCosmosId(request), GetPartitionKey(request));

        await Container.DeleteItemAsync<Request>(GetCosmosId(request), GetPartitionKey(request));

        Data.Request selectedRequest = response.Resource;

        if (selectedRequest.UserId == null) return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<User> GetUserByDisplayName(string displayName)
    {
        IQueryable<Data.User> usersQueryable = Queryable<Data.User>("User", Container).Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        List<Data.User> users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }
    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        IQueryable<Data.User> usersQueryable = Queryable<Data.User>("User", Container).Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        List<Data.User> users = await ToListAsync(usersQueryable);

        return Parse(users);
    }
    public async Task<User> GetUserById(Guid id)
    {
        Data.User user = new() { ID = id };
        ItemResponse<Data.User> response = await Container.ReadItemAsync<Data.User>(GetCosmosId(user), GetPartitionKey(user));

        return Parse(response.Resource);
    }

    public async Task<List<User>> GetUsers()
    {
        IQueryable<Data.User> usersQueryable = Queryable<Data.User>("User", Container);

        return Parse(await ToListAsync(usersQueryable));
    }

    public async Task CreateRequest(Guid userId, Guid requestId)
    {
        Data.Request request = new()
        {
            ID = requestId,
            UserId = userId
        };

        await Container.CreateItemAsync(request);

    }
    public async Task Update(User user)
    {
        Data.User dbUser = Parse(user);
        await Container.UpsertItemAsync(dbUser);
    }
    public async Task Create(User user)
    {
        Data.User dbUser = Parse(user);
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
        Data.User user = new() { ID = userId };
        await Container.DeleteItemStreamAsync(GetCosmosId(user), GetPartitionKey(user));
    }
}