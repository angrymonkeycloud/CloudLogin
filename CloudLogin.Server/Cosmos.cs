using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Mail;
using CloudUser = AngryMonkey.Cloud.Login.DataContract.CloudUser;

namespace AngryMonkey.Cloud.Login
{
    public class CosmosMethods
    {
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
            var container = Container.GetItemLinqQueryable<T>(requestOptions: new QueryRequestOptions())
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

        public async Task<CloudUser?> GetUserByEmailAddress(string emailAddress)
        {
            IQueryable<CloudUser> usersQueryable = Queryable<CloudUser>("CloudUser", user => user.Inputs.Where(key => key.Format == InputFormat.EmailAddress && key.Input.Equals(emailAddress.Trim(), StringComparison.OrdinalIgnoreCase)).Any());

            var users = await ToListAsync(usersQueryable);

            return users.FirstOrDefault();
        }

        public async Task<CloudUser?> GetUserByPhoneNumber(string number)
        {
            CloudGeographyClient geographyClient = new();

            return await GetUserByPhoneNumber(geographyClient.PhoneNumbers.Get(number));
        }

        public async Task<CloudUser?> GetUserByPhoneNumber(PhoneNumber phoneNumber)
        {
            CloudGeographyClient cloudGeography = new();

            IQueryable<CloudUser> usersQueryable = Queryable<CloudUser>("CloudUser", user
                => user.Inputs.Any(key => key.Format == InputFormat.PhoneNumber &&
                key.Input.Equals(phoneNumber.Number)
                    && (string.IsNullOrEmpty(phoneNumber.CountryCode)
                    || key.PhoneNumberCountryCode.Equals(phoneNumber.CountryCode, StringComparison.OrdinalIgnoreCase))));

            var users = await ToListAsync(usersQueryable);

            return users.FirstOrDefault();
        }

        public async Task<List<CloudUser>> GetUsersByDisplayName(string displayName)
        {
            IQueryable<CloudUser> usersQueryable = Queryable<CloudUser>("CloudUser").Where(key => key.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

            var users = await ToListAsync(usersQueryable);

            return users;
        }
        public async Task<CloudUser> GetUserById(Guid id)
        {
            CloudUser user = new() { ID = id };
            ItemResponse<CloudUser> response = await Container.ReadItemAsync<CloudUser>(GetCosmosId(user), GetPartitionKey(user));

            return response.Resource;
        }

        public async Task<List<CloudUser>> GetUsers()
        {
            IQueryable<CloudUser> usersQueryable = Queryable<CloudUser>("CloudUser");

            return await ToListAsync(usersQueryable);
        }

        public async Task AddInput(Guid userId, LoginInput Input)
        {
            CloudUser user = await GetUserById(userId);

            user.Inputs.Add(Input);

            await Container.UpsertItemAsync(user);
        }
        public async Task DeleteUser(Guid userId)
        {
            CloudUser user = new() { ID = userId };
            await Container.DeleteItemStreamAsync(GetCosmosId(user), GetPartitionKey(user));
        }
    }
}
