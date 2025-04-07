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
    [JsonProperty("id")] // to work with Cosmos
    public Guid ID { get; set; }


    [JsonPropertyName("$type")]
    [JsonProperty("$type")] // to work with Cosmos
    public string Type { get; internal set; }

    public string PartitionKey { get; internal set; }
}
