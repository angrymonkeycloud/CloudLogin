using AngryMonkey.CloudLogin.DataContract;
using AngryMonkey.CloudLogin.Providers;
using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin;

public class CloudLoginConfiguration
{
	public List<ProviderConfiguration> Providers { get; set; } = new();
	public TimeSpan LoginDuration { get; set; } = new TimeSpan(6*30, 0,0,0); //10 months
	public List<Link> FooterLinks { get; set; } = new();
	public string? RedirectUri { get; set; }
	public CosmosDatabase? Cosmos { get; set; }
	internal string? EmailMessageBody { get; set; }
	public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; } = null;
}

public class CosmosDatabase
{
	public string? ConnectionString { get; set; }
	public string? DatabaseId { get; set; }
	public string? ContainerId { get; set; }


    public CosmosDatabase(IConfigurationSection configurationSection)
    {
        ConnectionString = configurationSection["ConnectionString"];
        DatabaseId = configurationSection["DatabaseId"];
        ContainerId = configurationSection["ContainerId"];
    }
}
