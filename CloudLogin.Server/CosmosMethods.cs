using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin.Server;

public class CosmosMethods(CloudGeographyClient cloudGeography, Container container) : DataParse, ICloudLoginStore
{
    private readonly CloudGeographyClient CloudGeography = cloudGeography;
    private readonly Container _container = container;

    #region Internal

    internal static PartitionKey GetPartitionKey<T>(string partitionKey) =>
        !string.IsNullOrEmpty(partitionKey) ? new PartitionKey(partitionKey) : new PartitionKey(typeof(T).Name);

    internal static PartitionKey GetPartitionKey<T>(T record) where T : CloudLoginBaseRecord => new(record.PartitionKeyValue);

    #endregion

    #region SQL Query Helpers

    /// <summary>
    /// Builds a WHERE clause that checks both modern and legacy property names for type/discriminator when IncludeLegacySchema is enabled
    /// </summary>
    private static string BuildTypeCondition(string userType)
    {
        string typePropertyName = CloudLoginBaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = CloudLoginBaseRecord.GetPartitionKeyJsonPropertyName();
        
        if (CloudLoginBaseRecord.ShouldIncludeLegacySchema())
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

    public async Task<CloudUser?> GetUserByEmailAddress(string emailAddress)
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");
        string typeCondition = BuildTypeCondition(userType);
        
        // Note: use normal escaped quotes (\") so Cosmos SQL doesn't see backslashes
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND EXISTS(SELECT VALUE 1 FROM input IN root.Inputs WHERE input.Format = \"EmailAddress\" AND UPPER(input.Input) = UPPER(@emailAddress))";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@emailAddress", emailAddress.Trim());

        FeedIterator<CloudUserInfo> iterator = _container.GetItemQueryIterator<CloudUserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        List<CloudUserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudUserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users.FirstOrDefault());
    }

    public async Task<CloudUser?> GetUserByInput(string input)
    {
        CloudUser? user = await GetUserByEmailAddress(input);

        if (user == null)
            return await GetUserByPhoneNumber(CloudGeography.PhoneNumbers.Get(input));

        return user;
    }

    public async Task<CloudUser?> GetUserByPhoneNumber(string number)
    {
        if (string.IsNullOrEmpty(number))
            return null;

        return await GetUserByPhoneNumber(CloudGeography.PhoneNumbers.Get(number));
    }

    public async Task<CloudUser?> GetUserByPhoneNumber(PhoneNumber phoneNumber)
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND EXISTS(SELECT VALUE 1 FROM input IN root.Inputs WHERE input.Format = \"PhoneNumber\" AND input.Input = @phoneNumber AND (@countryCode = \"\" OR input.PhoneNumberCountryCode = @countryCode))";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@phoneNumber", phoneNumber.Number)
            .WithParameter("@countryCode", phoneNumber.CountryCode ?? string.Empty);

        FeedIterator<CloudUserInfo> iterator = _container.GetItemQueryIterator<CloudUserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        List<CloudUserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudUserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users.FirstOrDefault());
    }

    public async Task<CloudUser?> GetUserByRequestId(Guid requestId)
    {
        CloudRequest request = new();
        request.SetId(requestId);
        
        // When using legacy schema with TypePrefixed save mode, use the formatted ID
        string documentId = request.GetFormattedId();
        
        ItemResponse<CloudRequest> response = await _container.ReadItemAsync<CloudRequest>(documentId, GetPartitionKey(request));
        await _container.DeleteItemAsync<CloudRequest>(documentId, GetPartitionKey(request));
        CloudRequest selectedRequest = response.Resource;

        if (selectedRequest.UserId == null)
            return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<CloudUser?> GetUserByDisplayName(string displayName)
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND UPPER(root.DisplayName) = UPPER(@displayName)";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@displayName", displayName);

        FeedIterator<CloudUserInfo> iterator = _container.GetItemQueryIterator<CloudUserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        List<CloudUserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudUserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users.FirstOrDefault());
    }

    public async Task<List<CloudUser>> GetUsersByDisplayName(string displayName)
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition} AND UPPER(root.DisplayName) = UPPER(@displayName)";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType)
            .WithParameter("@displayName", displayName);

        FeedIterator<CloudUserInfo> iterator = _container.GetItemQueryIterator<CloudUserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        List<CloudUserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudUserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users) ?? [];
    }

    public async Task<CloudUser?> GetUserById(Guid id)
    {
        CloudUserInfo user = new();
        user.SetId(id);
        
        // When using legacy schema with TypePrefixed save mode, use the formatted ID
        string documentId = user.GetFormattedId();
        
        ItemResponse<CloudUserInfo> response = await _container.ReadItemAsync<CloudUserInfo>(documentId, GetPartitionKey(user));

        return Parse(response.Resource);
    }

    public async Task<List<CloudUser>> GetUsers()
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");
        string typeCondition = BuildTypeCondition(userType);
        
        string sql = $"SELECT VALUE root FROM root WHERE {typeCondition}";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType);

        FeedIterator<CloudUserInfo> iterator = _container.GetItemQueryIterator<CloudUserInfo>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        List<CloudUserInfo> users = [];
        while (iterator.HasMoreResults)
        {
            FeedResponse<CloudUserInfo> response = await iterator.ReadNextAsync();
            users.AddRange(response);
        }

        return Parse(users) ?? [];
    }

    public async Task<CloudRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        CloudRequest request = new();
        request.SetId(requestId ?? Guid.NewGuid());
        request.UserId = userId;
        await _container.CreateItemAsync(request, GetPartitionKey(request));

        return request;
    }

    public async Task Update(CloudUser user)
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
                CloudUser? existing = await GetUserByInput(candidate);
                if (existing != null)
                    user.ID = existing.ID;
            }

            if (user.ID == Guid.Empty)
                throw new InvalidOperationException("Cannot update user with empty ID. Provide a valid ID or use Create.");
        }

        CloudUserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));
        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task UpdateLastSignedIn(Guid userId, DateTimeOffset lastSignedIn)
    {
        CloudUserInfo userInfo = new();
        userInfo.SetId(userId);
        PartitionKey partitionKey = GetPartitionKey(userInfo);

        // When using legacy schema with TypePrefixed save mode, use the formatted ID
        string documentId = userInfo.GetFormattedId();

        string lastSignedInPath = "/LastSignedIn";
        List<PatchOperation> patchOperations = [PatchOperation.Replace(lastSignedInPath, lastSignedIn)];

        await _container.PatchItemAsync<CloudUserInfo>(documentId, partitionKey, patchOperations);
    }

    public async Task<int> GetUserCount()
    {
        string userType = CloudLoginBaseRecord.GetEffectiveTypeValue("UserInfo");
        string typeCondition = BuildTypeCondition(userType);

        string sql = $"SELECT VALUE COUNT(1) FROM root WHERE {typeCondition}";

        QueryDefinition queryDefinition = CreateUserQueryDefinition(sql, userType);

        FeedIterator<int> iterator = _container.GetItemQueryIterator<int>(
            queryDefinition,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userType) });

        int count = 0;
        
        while (iterator.HasMoreResults)
        {
            FeedResponse<int> response = await iterator.ReadNextAsync();
            
            foreach (int value in response)
                count += value;
        }

        return count;
    }

    public async Task Create(CloudUser user)
    {
        // The first user ever created becomes a Global Admin
        if (!user.IsGlobalAdmin)
        {
            int existingCount = await GetUserCount();

            if (existingCount == 0)
                user.IsGlobalAdmin = true;
        }

        CloudUserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));

        if (dbUser.GetId() == Guid.Empty)
            dbUser.SetId(Guid.NewGuid());

        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task AddInput(Guid userId, CloudLoginInput Input)
    {
        CloudUser user = await GetUserById(userId) ?? throw new Exception("User not found.");
        user.Inputs.Add(Input);
        
        CloudUserInfo dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));
        await _container.UpsertItemAsync(dbUser, GetPartitionKey(dbUser));
    }

    public async Task DeleteUser(Guid userId)
    {
        CloudUserInfo user = new();
        user.SetId(userId);
        
        // When using legacy schema with TypePrefixed save mode, use the formatted ID
        string documentId = user.GetFormattedId();
        
        await _container.DeleteItemStreamAsync(documentId, GetPartitionKey(user));
    }
}
