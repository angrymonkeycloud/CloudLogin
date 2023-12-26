using AngryMonkey.CloudLogin.Providers;
using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin;

public class CloudLoginConfiguration
{
	public required List<ProviderConfiguration> Providers { get; set; } = [];
	public string? BaseAddress { get; set; }
	public TimeSpan LoginDuration { get; set; } = new TimeSpan(6*30, 0,0,0); //10 months
	public List<Link> FooterLinks { get; set; } = new();
	public string? RedirectUri { get; set; }
	public CosmosDatabase? Cosmos { get; set; }
	internal string? EmailMessageBody { get; set; }
	public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; } = null;
}

public class CosmosDatabase(IConfigurationSection configurationSection)
{
    public string ConnectionString { get; set; } = configurationSection["ConnectionString"]!;
    public string DatabaseId { get; set; } = configurationSection["DatabaseId"]!;
    public string ContainerId { get; set; } = configurationSection["ContainerId"]!;
}
