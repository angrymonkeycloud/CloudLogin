using Microsoft.AspNetCore.Mvc;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.Azure.Cosmos;
using System.Configuration;

namespace AngryMonkey.Cloud.Login.Controllers
{
	public class BaseController : Controller
	{
		public static CloudLoginConfiguration Configuration;

		private CosmosMethods _cosmosMethods;
		internal CosmosMethods CosmosMethods
		{
			get
			{
				if (Configuration.Cosmos == null)
					throw new Exception();
				return _cosmosMethods ??= new CosmosMethods(Configuration.Cosmos.ConnectionString, Configuration.Cosmos.DatabaseId, Configuration.Cosmos.ContainerId);

			}
		}
	}
}
