using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server;

public abstract record BaseRecord
{
    // Static configuration for dynamic property naming
    public static CosmosConfiguration? CosmosConfiguration { get; set; }

    internal BaseRecord(string partitionKey, string type)
    {
        PartitionKey = partitionKey;
        Type = type;
    }

    // Remove hardcoded JSON attributes - will be handled by custom converter
    public Guid ID { get; set; }
    public string Type { get; internal set; }
    public string PartitionKey { get; internal set; }

    // Methods to get configured property names
    public static string GetTypePropertyName() => CosmosConfiguration?.TypeName ?? "$type";
    public static string GetPartitionKeyPropertyName() => CosmosConfiguration?.PartitionKeyName ?? "/pk";
    
    // Method to get the partition key path for Cosmos container configuration
    public static string GetPartitionKeyPath() => CosmosConfiguration?.PartitionKeyName ?? "/pk";
    
    // Get the JSON property name for PartitionKey (without the leading slash)
    public static string GetPartitionKeyJsonPropertyName() => GetPartitionKeyPath().TrimStart('/');
}
