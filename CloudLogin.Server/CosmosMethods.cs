﻿using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq.Expressions;

namespace AngryMonkey.CloudLogin.Server;

public class CosmosMethods : DataParse, IDisposable
{
    private readonly CloudGeographyClient CloudGeography;

    private readonly CosmosClient _client;
    private readonly Container _container;

    public CosmosMethods(CosmosConfiguration cosmosConfiguration, CloudGeographyClient cloudGeography)
    {
        CloudGeography = cloudGeography;

        _client = new(cosmosConfiguration.ConnectionString, new CosmosClientOptions() { SerializerOptions = new() { IgnoreNullValues = true } });

        Task.Run(async () =>
        {
            DatabaseResponse database = await _client.CreateDatabaseIfNotExistsAsync(cosmosConfiguration.DatabaseId);

            await database.Database.CreateContainerIfNotExistsAsync(new(cosmosConfiguration.ContainerId, "/PartitionKey"));
        }).Wait();

        _container = _client.GetContainer(cosmosConfiguration.DatabaseId, cosmosConfiguration.ContainerId);
    }

    public void Dispose() => _client.Dispose();

    #region Internal

    internal IQueryable<T> Queryable<T>(string partitionKey) where T : BaseRecord
    {
        return Queryable<T>(partitionKey, null);
    }

    internal static PartitionKey GetPartitionKey<T>(string partitionKey)
    {
        if (!string.IsNullOrEmpty(partitionKey))
            return new PartitionKey(partitionKey);

        return new PartitionKey(typeof(T).Name);
    }

    internal IQueryable<T> Queryable<T>(string partitionKey, Expression<Func<T, bool>>? predicate) where T : BaseRecord
    {
        var container = _container.GetItemLinqQueryable<T>(requestOptions: new QueryRequestOptions())
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
            List<T> list = [];

            using FeedIterator<T> setIterator = query.ToFeedIterator();

            while (setIterator.HasMoreResults)
                foreach (var item in await setIterator.ReadNextAsync())
                    list.Add(item);

            return list;
        }
        catch
        {
            throw;
        }
    }

    internal static PartitionKey GetPartitionKey<T>(T record) where T : BaseRecord => new(record.PartitionKey);

    internal static string GetCosmosId<T>(T record) where T : BaseRecord => $"{record.Discriminator}|{record.ID}";

    #endregion

    public async Task<User?> GetUserByEmailAddress(string emailAddress)
    {
        IQueryable<DataUser> usersQueryable = Queryable<DataUser>("User", user => user.Inputs.Any(key => key.Format == InputFormat.EmailAddress && key.Input.Equals(emailAddress.Trim(), StringComparison.OrdinalIgnoreCase)));

        List<DataUser> users = await ToListAsync(usersQueryable);

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
        IQueryable<DataUser> usersQueryable = Queryable<DataUser>("User", user
            => user.Inputs.Any(key => key.Format == InputFormat.PhoneNumber &&
            key.Input.Equals(phoneNumber.Number)
                && (string.IsNullOrEmpty(phoneNumber.CountryCode)
                || key.PhoneNumberCountryCode.Equals(phoneNumber.CountryCode, StringComparison.OrdinalIgnoreCase))));

        List<DataUser> users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        LoginRequest request = new() { ID = requestId };

        ItemResponse<LoginRequest> response = await _container.ReadItemAsync<LoginRequest>(GetCosmosId(request), GetPartitionKey(request));

        await _container.DeleteItemAsync<LoginRequest>(GetCosmosId(request), GetPartitionKey(request));

        LoginRequest selectedRequest = response.Resource;

        if (selectedRequest.UserId == null)
            return null;

        return await GetUserById(selectedRequest.UserId.Value);
    }

    public async Task<User?> GetUserByDisplayName(string displayName)
    {
        IQueryable<DataUser> usersQueryable = Queryable<DataUser>("User").Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        List<DataUser> users = await ToListAsync(usersQueryable);

        return Parse(users.FirstOrDefault());
    }

    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        IQueryable<DataUser> usersQueryable = Queryable<DataUser>("User").Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        List<DataUser> users = await ToListAsync(usersQueryable);

        return Parse(users) ?? [];
    }

    public async Task<User?> GetUserById(Guid id)
    {
        DataUser user = new() { ID = id };
        ItemResponse<DataUser> response = await _container.ReadItemAsync<DataUser>(GetCosmosId(user), GetPartitionKey(user));

        return Parse(response.Resource);
    }

    public async Task<List<User>> GetUsers()
    {
        IQueryable<DataUser> usersQueryable = Queryable<DataUser>("User");

        return Parse(await ToListAsync(usersQueryable)) ?? [];
    }

    public async Task<LoginRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        LoginRequest request = new() { ID = requestId ?? Guid.NewGuid(), UserId = userId };

        await _container.CreateItemAsync(request);

        return request;
    }

    public async Task Update(User user)
    {
        DataUser dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));

        await _container.UpsertItemAsync(dbUser);
    }

    public async Task Create(User user)
    {
        DataUser dbUser = Parse(user) ?? throw new NullReferenceException(nameof(user));

        await _container.CreateItemAsync(dbUser);
    }

    public async Task AddInput(Guid userId, LoginInput Input)
    {
        User user = await GetUserById(userId) ?? throw new Exception("User not found.");

        user.Inputs.Add(Input);

        await _container.UpsertItemAsync(user);
    }

    public async Task DeleteUser(Guid userId)
    {
        DataUser user = new() { ID = userId };
        await _container.DeleteItemStreamAsync(GetCosmosId(user), GetPartitionKey(user));
    }
}