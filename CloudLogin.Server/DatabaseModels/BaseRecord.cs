using Newtonsoft.Json;

namespace AngryMonkey.CloudLogin;

public record BaseRecord
{
    internal BaseRecord(string partitionKey, string discriminator)
    {
        PartitionKey = partitionKey;
        Discriminator = discriminator;
    }

    [JsonProperty("id")]
    internal string CosmosId => $"{Discriminator}|{ID}";

    [JsonProperty("ID")]
    public Guid ID { get; set; }
    public string PartitionKey { get; internal set; }
    public string Discriminator { get; internal set; }
}
