using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq.Expressions;
using User = AngryMonkey.Cloud.Login.DataContract.User;

namespace AngryMonkey.Cloud.Login
{
	public class CosmosMethods
	{
		#region Internal

		public Container Container { get; set; }

		internal CosmosMethods(string connectionString, string databaseId, string containerId)
		{
			CosmosClient client = new(connectionString, new CosmosClientOptions()
			{
				SerializerOptions = new()
				{
					IgnoreNullValues = true
				}
			});

			Container = client.GetContainer(databaseId, containerId);
		}

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
				throw e;
			}
		}

		#endregion

		public async Task<User?> GetUserByEmailAddress(string emailAddress)
		{
			IQueryable<User> usersQueryable = Queryable<User>("User", user => user.EmailAddresses.Where(key => key.EmailAddress.Equals(emailAddress.Trim(), StringComparison.OrdinalIgnoreCase)).Any());

			var users = await ToListAsync(usersQueryable);

			return users.FirstOrDefault();
		}
	}
}
