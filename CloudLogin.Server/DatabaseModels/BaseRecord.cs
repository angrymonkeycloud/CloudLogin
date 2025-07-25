using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server;

public record BaseRecord
{
    internal BaseRecord(string partitionKey, string type)
    {
        PartitionKey = partitionKey;
        Type = type;
    }

    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    [JsonProperty("id", Order = 0)] // to work with Cosmos
    public Guid ID { get; set; }


    [JsonPropertyName("$type")]
    [JsonPropertyOrder(1)]
    [JsonProperty("$type", Order = 1)] // to work with Cosmos
    public string Type { get; internal set; }

    [JsonPropertyOrder(2)]
    [JsonProperty(Order = 2)] // to work with Cosmos
    public string PartitionKey { get; internal set; }
}
