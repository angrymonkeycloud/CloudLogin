using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server;

public record BaseRecord
{
    internal BaseRecord(string partitionKey, string discriminator)
    {
        PartitionKey = partitionKey;
        Discriminator = discriminator;
    }

    [JsonPropertyName("id")]
    [JsonProperty("id")] // to work with Cosmos
    internal string CosmosId => $"{Discriminator}|{ID}";

    [JsonPropertyName("ID")]
    [JsonProperty("ID")] // to work with Cosmos
    public Guid ID { get; set; }
    public string PartitionKey { get; internal set; }
    public string Discriminator { get; internal set; }
}
