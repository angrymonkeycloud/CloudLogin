using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

public record BaseRecord
{
    internal BaseRecord(string partitionKey, string discriminator)
    {
        PartitionKey = partitionKey;
        Discriminator = discriminator;
    }

    [JsonPropertyName("id")]
    internal string CosmosId => $"{Discriminator}|{ID}";

    [JsonPropertyName("ID")]
    public Guid ID { get; set; }
    public string PartitionKey { get; internal set; }
    public string Discriminator { get; internal set; }
}
