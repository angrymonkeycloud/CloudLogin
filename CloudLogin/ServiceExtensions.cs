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

	public static CloudLoginService AddCloudLogin(this IServiceCollection services, HttpClient? httpServer = null)
	{
		CloudGeographyClient cloudGeography = new();

		CloudLoginClient cloudLoginClient = new() { HttpClient = httpServer };
		cloudLoginClient.FooterLinks.Add(new Link()
		{
			Url = "https://angrymonkeycloud.com/",
			Title = "Info"
		});

		services.AddSingleton(new CloudLoginService());
		services.AddSingleton(cloudGeography);
		services.AddSingleton(cloudLoginClient);

		return null;
	}
}