using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Linq.Expressions;

namespace AngryMonkey.Cloud.Login
{
	internal class CosmosMethods
	{
		public Container Container { get; set; }

		public CosmosMethods(string connectionString, string databaseId, string containerId)
		{
			CosmosClient client = new(connectionString, new CosmosClientOptions()
			{
				SerializerOptions = new CosmosSerializationOptions()
				{
					IgnoreNullValues = true
				}
			});

			Container = client.GetContainer(databaseId, containerId);
		}

		public IQueryable<T> Queryable<T>(string partitionKey) where T : BaseRecord
		{
			return Queryable<T>(partitionKey, null);
		}

		private static PartitionKey GetPartitionKey<T>(string partitionKey)
		{
			if (!string.IsNullOrEmpty(partitionKey))
				return new PartitionKey(partitionKey);

			return new PartitionKey(typeof(T).Name);
		}

		public IQueryable<T> Queryable<T>(string partitionKey, Expression<Func<T, bool>>? predicate) where T : BaseRecord
		{
			var container = Container.GetItemLinqQueryable<T>(requestOptions: new QueryRequestOptions())
									 .Where(key => key.Discriminator == typeof(T).Name);

			if (!string.IsNullOrEmpty(partitionKey))
				container = container.Where(key => key.PartitionKey == partitionKey);

			if (predicate != null)
				container = container.Where(predicate);

			return container;
		}

		public async Task<List<T>> ToListAsync<T>(IQueryable<T> query) where T : BaseRecord
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

		//public async Task<T> Create(string partitionKey, T record) where T : BaseRecord
		//{
		//	return await Container.CreateItemAsync(record, GetPartitionKey<T>(partitionKey));
		//}
	}
}
