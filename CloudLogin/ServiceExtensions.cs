using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.DataContract;

namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginService
{
	IServiceCollection AddCloudLogin { get; }
	//public CloudLoginConfiguration Options { get; set; }
}

public static class MvcServiceCollectionExtensions
{
	public static async Task<CloudLoginService> AddCloudLogin(this IServiceCollection services, HttpClient? httpServer = null)
	{
		CloudLoginClient cloudLoginClient = new() { HttpClient = httpServer };

        cloudLoginClient = await cloudLoginClient.InitFromServer();

        cloudLoginClient.FooterLinks.Add(new Link()
		{
			Url = "https://angrymonkeycloud.com/",
			Title = "Info"
		});

		services.AddSingleton(cloudLoginClient);

		return null;
    }
}