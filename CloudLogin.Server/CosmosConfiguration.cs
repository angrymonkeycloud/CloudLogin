using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Server;

public class CosmosConfiguration
{
    public CosmosConfiguration(IConfigurationSection configurationSection)
    {
        AspireName = configurationSection["AspireName"];
        ConnectionString = configurationSection["ConnectionString"];
        DatabaseId = configurationSection["DatabaseId"];
        ContainerId = configurationSection["ContainerId"] ?? "Users";
        PartitionKeyName = configurationSection["PartitionKeyName"] ?? "/pk";
        TypeName = configurationSection["TypeName"] ?? "$type";
    }

    public CosmosConfiguration() { }

    public string? AspireName { get; set; }
    public string? ConnectionString { get; set; }
    public string? DatabaseId { get; set; }
    public string? ContainerId { get; set; }
    public string PartitionKeyName { get; set; } = "/pk";
    public string TypeName { get; set; } = "$type";

    public bool IsValid() => !string.IsNullOrEmpty(AspireName) || !string.IsNullOrEmpty(ConnectionString);
}
