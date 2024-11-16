using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin;

public class CosmosConfiguration
{
    public CosmosConfiguration(IConfigurationSection configurationSection)
    {
        ConnectionString = configurationSection["ConnectionString"];
        DatabaseId = configurationSection["DatabaseId"];
        ContainerId = configurationSection["ContainerId"];
    }

    public CosmosConfiguration() { }

    public string? ConnectionString { get; set; }
    public string? DatabaseId { get; set; }
    public string? ContainerId { get; set; } = "Users";
}
