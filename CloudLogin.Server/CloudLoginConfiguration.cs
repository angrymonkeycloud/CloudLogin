namespace AngryMonkey.Cloud.Login.DataContract;

public class CloudLoginConfiguration
{
	//public HttpClient? HttpServer { get; set; }
	public List<ProviderConfiguration> Providers { get; set; } = new();
	public CosmosDatabase? Cosmos { get; set; }
	internal string EmailMessageBody { get; set; }
	public Func<SendCodeValue, Task>? EmailSendCodeRequest { get; set; } = null;
}

public class CosmosDatabase
{
	public string ConnectionString { get; set; }
	public string DatabaseId { get; set; }
	public string ContainerId { get; set; }
}
