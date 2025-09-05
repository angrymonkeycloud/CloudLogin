using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq.Expressions;

namespace AngryMonkey.CloudLogin.Server;

public class CosmosMethods(CloudGeographyClient cloudGeography, Container container) : DataParse
{
    private readonly CloudGeographyClient CloudGeography = cloudGeography;
    private readonly Container _container = container;

    #region Internal

    internal IQueryable<T> Queryable<T>(string partitionKey) where T : BaseRecord => Queryable<T>(partitionKey, null);

    internal static PartitionKey GetPartitionKey<T>(string partitionKey) =>
        !string.IsNullOrEmpty(partitionKey) ? new PartitionKey(partitionKey) : new PartitionKey(typeof(T).Name);

    internal IQueryable<T> Queryable<T>(string partitionKey, Expression<Func<T, bool>>? predicate) where T : BaseRecord
    {
        QueryRequestOptions options = new();

        if (!string.IsNullOrEmpty(partitionKey))
            options.PartitionKey = new PartitionKey(partitionKey);

        // Don't filter by Type or PartitionKey in LINQ - let the JSON converter handle it
        IQueryable<T> query = _container.GetItemLinqQueryable<T>(requestOptions: options);

        if (predicate != null)
            query = query.Where(predicate);

        return query;
    }

    internal static async Task<List<T>> ToListAsync<T>(IQueryable<T> query) where T : BaseRecord
    {
        List<T> list = [];
        using FeedIterator<T> setIterator = query.ToFeedIterator();

        while (setIterator.HasMoreResults)
            foreach (var item in await setIterator.ReadNextAsync())
                list.Add(item);

        return list;
    }

    internal static PartitionKey GetPartitionKey<T>(T record) where T : BaseRecord => new(record.PartitionKey);

    #endregion

    public async Task<User?> GetUserByEmailAddress(string emailAddress)
    {
        // Use raw SQL query to avoid LINQ property name translation issues
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        string sql = $"SELECT VALUE root FROM root WHERE root[\"{typePropertyName}\"] = \"UserInfo\" AND root[\"{partitionKeyPropertyName}\"] = \"UserInfo\" AND EXISTS(SELECT VALUE 1 FROM input IN root.Inputs WHERE input.Format = \"EmailAddress\" AND UPPER(input.Input) = UPPER(@emailAddress))";

        QueryDefinition queryDefinition = new QueryDefinition(sql)
            .WithParameter("@emailAddress", emailAddress.Trim());

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition, 
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("UserInfo") });

        List<UserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByInput(string input)
    {
        User? user = await GetUserByEmailAddress(input);

        if (user == null)
            return await GetUserByPhoneNumber(CloudGeography.PhoneNumbers.Get(input));

        return user;
    }

    public async Task<User?> GetUserByPhoneNumber(string number)
    {
        if (string.IsNullOrEmpty(number))
            return null;

        return await GetUserByPhoneNumber(CloudGeography.PhoneNumbers.Get(number));
    }

    public async Task<User?> GetUserByPhoneNumber(PhoneNumber phoneNumber)
    {
        // Use raw SQL query to avoid LINQ property name translation issues
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        string sql = $"SELECT VALUE root FROM root WHERE root[\"{typePropertyName}\"] = \"UserInfo\" AND root[\"{partitionKeyPropertyName}\"] = \"UserInfo\" AND EXISTS(SELECT VALUE 1 FROM input IN root.Inputs WHERE input.Format = \"PhoneNumber\" AND input.Input = @phoneNumber AND (@countryCode = \"\" OR input.PhoneNumberCountryCode = @countryCode))";

        QueryDefinition queryDefinition = new QueryDefinition(sql)
            .WithParameter("@phoneNumber", phoneNumber.Number)
            .WithParameter("@countryCode", phoneNumber.CountryCode ?? "");

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition, 
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("UserInfo") });

        List<UserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        LoginRequest request = new() { ID = requestId };
        ItemResponse<LoginRequest> response = await _container.ReadItemAsync<LoginRequest>(requestId.ToString(), GetPartitionKey(request));
        await _container.DeleteItemAsync<LoginRequest>(requestId.ToString(), GetPartitionKey(request));
        LoginRequest selectedRequest = response.Resource;

        if (selectedRequest.UserId == null)
            return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<User?> GetUserByDisplayName(string displayName)
    {
        // Use raw SQL query to avoid LINQ property name translation issues
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        string sql = $"SELECT VALUE root FROM root WHERE root[\"{typePropertyName}\"] = \"UserInfo\" AND root[\"{partitionKeyPropertyName}\"] = \"UserInfo\" AND UPPER(root.DisplayName) = UPPER(@displayName)";

        QueryDefinition queryDefinition = new QueryDefinition(sql)
            .WithParameter("@displayName", displayName);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition, 
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("UserInfo") });

        List<UserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users.FirstOrDefault());
    }

    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        // Use raw SQL query to avoid LINQ property name translation issues
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        string sql = $"SELECT VALUE root FROM root WHERE root[\"{typePropertyName}\"] = \"UserInfo\" AND root[\"{partitionKeyPropertyName}\"] = \"UserInfo\" AND UPPER(root.DisplayName) = UPPER(@displayName)";

        QueryDefinition queryDefinition = new QueryDefinition(sql)
            .WithParameter("@displayName", displayName);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition, 
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("UserInfo") });

        List<UserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users) ?? [];
    }

    public async Task<User?> GetUserById(Guid id)
    {
        UserInfo user = new() { ID = id };
        ItemResponse<UserInfo> response = await _container.ReadItemAsync<UserInfo>(id.ToString(), GetPartitionKey(user));

        return Parse(response.Resource);
    }

    public async Task<List<User>> GetUsers()
    {
        // Use raw SQL query to avoid LINQ property name translation issues
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        string sql = $"SELECT VALUE root FROM root WHERE root[\"{typePropertyName}\"] = \"UserInfo\" AND root[\"{partitionKeyPropertyName}\"] = \"UserInfo\"";

        QueryDefinition queryDefinition = new QueryDefinition(sql);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition, 
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("UserInfo") });

        List<UserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users) ?? [];
    }

    public async Task<LoginRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        LoginRequest request = new() { ID = requestId ?? Guid.NewGuid(), UserId = userId };
        await _container.CreateItemAsync(request, GetPartitionKey(request));

        return request;
    }

    public async Task Update(User user)
    {
        UserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));
        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task UpdateLastSignedIn(Guid userId, DateTimeOffset lastSignedIn)
    {
        UserInfo userInfo = new() { ID = userId };
        PartitionKey partitionKey = GetPartitionKey(userInfo);

        // Note: Patch operations use JSON property paths, so we need to use configured names
        string lastSignedInPath = "/LastSignedIn"; // This should work as property names are preserved
        List<PatchOperation> patchOperations = [PatchOperation.Replace(lastSignedInPath, lastSignedIn)];

        await _container.PatchItemAsync<UserInfo>(userId.ToString(), partitionKey, patchOperations);
    }

    public async Task Create(User user)
    {
        UserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));
        await _container.CreateItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task AddInput(Guid userId, LoginInput Input)
    {
        User user = await GetUserById(userId) ?? throw new Exception("User not found.");
        user.Inputs.Add(Input);
        await _container.UpsertItemAsync(user);
    }

    public async Task DeleteUser(Guid userId)
    {
        UserInfo user = new() { ID = userId };
        await _container.DeleteItemStreamAsync(userId.ToString(), GetPartitionKey(user));
    }
}
