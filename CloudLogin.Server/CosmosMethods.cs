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
            foreach (T item in await setIterator.ReadNextAsync())
                list.Add(item);

        return list;
    }

    internal static PartitionKey GetPartitionKey<T>(T record) where T : BaseRecord => new(record.PartitionKeyValue);

    #endregion

    #region SQL Query Helpers

    /// <summary>
    /// Builds a WHERE clause that checks both modern and legacy property names for type/discriminator when IncludeLegacySchema is enabled
    /// </summary>
    private static string BuildTypeCondition(string userType)
    {
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        if (BaseRecord.ShouldIncludeLegacySchema())
        {
            // When legacy schema is included, check both modern and legacy property names
            // This handles cases where data might exist with either naming convention
            return $@"(
                (root[""{typePropertyName}""] = @userType OR root[""Discriminator""] = @userType) AND 
                (root[""{partitionKeyPropertyName}""] = @userType OR root[""PartitionKey""] = @userType)
            )";
        }
        else
        {
            // Standard mode - only check modern property names
            return $@"root[""{typePropertyName}""] = @userType AND root[""{partitionKeyPropertyName}""] = @userType";
        }
    }

    /// <summary>
    /// Creates a QueryDefinition with proper parameter setup for the user type
    /// </summary>
    private static QueryDefinition CreateUserQueryDefinition(string sql, string userType)
    {
        return new QueryDefinition(sql).WithParameter("@userType", userType);
    }

    #endregion

    public async Task<User?> GetUserByEmailAddress(string emailAddress)
    {
        string userType = BaseRecord.GetEffectiveTypeValue(nameof(UserInfo));
        string typeCondition = BuildTypeCondition(userType);
        
        // Note: use normal escaped quotes (\") so Cosmos SQL doesn't see backslashes
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND EXISTS(SELECT VALUE 1 FROM input IN root.Inputs WHERE input.Format = \"EmailAddress\" AND UPPER(input.Input) = UPPER(@emailAddress))";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@emailAddress", emailAddress.Trim());

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

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
        string userType = BaseRecord.GetEffectiveTypeValue(nameof(UserInfo));
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND EXISTS(SELECT VALUE 1 FROM input IN root.Inputs WHERE input.Format = \"PhoneNumber\" AND input.Input = @phoneNumber AND (@countryCode = \"\" OR input.PhoneNumberCountryCode = @countryCode))";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@phoneNumber", phoneNumber.Number)
            .WithParameter("@countryCode", phoneNumber.CountryCode ?? string.Empty);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

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
        LoginRequest request = new();
        request.SetId(requestId);
        ItemResponse<LoginRequest> response = await _container.ReadItemAsync<LoginRequest>(requestId.ToString(), GetPartitionKey(request));
        await _container.DeleteItemAsync<LoginRequest>(requestId.ToString(), GetPartitionKey(request));
        LoginRequest selectedRequest = response.Resource;

        if (selectedRequest.UserId == null)
            return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<User?> GetUserByDisplayName(string displayName)
    {
        string userType = BaseRecord.GetEffectiveTypeValue(nameof(UserInfo));
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND UPPER(root.DisplayName) = UPPER(@displayName)";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@displayName", displayName);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

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
        string userType = BaseRecord.GetEffectiveTypeValue(nameof(UserInfo));
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND UPPER(root.DisplayName) = UPPER(@displayName)";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@displayName", displayName);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

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
        UserInfo user = new();
        user.SetId(id);
        ItemResponse<UserInfo> response = await _container.ReadItemAsync<UserInfo>(id.ToString(), GetPartitionKey(user));

        return Parse(response.Resource);
    }

    public async Task<List<User>> GetUsers()
    {
        string userType = BaseRecord.GetEffectiveTypeValue(nameof(UserInfo));
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition}";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType);

        FeedIterator<UserInfo> iterator = _container.GetItemQueryIterator<UserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

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
        LoginRequest request = new();
        request.SetId(requestId ?? Guid.NewGuid());
        request.UserId = userId;
        await _container.CreateItemAsync(request, GetPartitionKey(request));

        return request;
    }

    public async Task Update(User user)
    {
        // Do not generate a new ID on updates.
        if (user.ID == Guid.Empty)
        {
            // Try to resolve existing user by any available input (prefer primary email, then first email, then any input)
            string? candidate = user.PrimaryEmailAddress?.Input
                               ?? user.EmailAddresses?.FirstOrDefault()?.Input
                               ?? user.Inputs?.FirstOrDefault()?.Input;

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                User? existing = await GetUserByInput(candidate);
                if (existing != null)
                    user.ID = existing.ID;
            }

            if (user.ID == Guid.Empty)
                throw new InvalidOperationException("Cannot update user with empty ID. Provide a valid ID or use Create.");
        }

        UserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));
        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task UpdateLastSignedIn(Guid userId, DateTimeOffset lastSignedIn)
    {
        UserInfo userInfo = new();
        userInfo.SetId(userId);
        PartitionKey partitionKey = GetPartitionKey(userInfo);

        string lastSignedInPath = "/LastSignedIn";
        List<PatchOperation> patchOperations = [PatchOperation.Replace(lastSignedInPath, lastSignedIn)];

        await _container.PatchItemAsync<UserInfo>(userId.ToString(), partitionKey, patchOperations);
    }

    public async Task Create(User user)
    {
        UserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));

        if (dbUser.GetId() == Guid.Empty)
            dbUser.SetId(Guid.NewGuid());

        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task AddInput(Guid userId, LoginInput Input)
    {
        User user = await GetUserById(userId) ?? throw new Exception("User not found.");
        user.Inputs.Add(Input);
        
        UserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));
        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task DeleteUser(Guid userId)
    {
        UserInfo user = new();
        user.SetId(userId);
        await _container.DeleteItemStreamAsync(userId.ToString(), GetPartitionKey(user));
    }
}
