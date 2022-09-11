using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.AspNetCore.Builder
{
	public static class CloudLoginBuilderExtensions
	{
		public static IApplicationBuilder UseCloudLogin(this IApplicationBuilder app)
		{
			if (app == null)
				throw new ArgumentNullException(nameof(app));

			return app;
		}
	}
}
