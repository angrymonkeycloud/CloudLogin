using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server;

public abstract record CloudLoginBaseRecord
{
    // Static configuration for dynamic property naming
    public static CosmosConfiguration? CosmosConfiguration { get; set; }

    internal CloudLoginBaseRecord(string partitionKey, string type)
    {
        PartitionKeyValue = partitionKey;
        TypeValue = type;
    }

    // Internal GUID storage - not a public property
    [JsonIgnore]
    internal Guid InternalId { get; set; }

    // Keep the raw JSON id value to handle any deserialization ordering edge cases
    [JsonIgnore]
    private string? _rawJsonId;

    // Cosmos DB requires a lowercase raw-GUID id property.
    [JsonPropertyName("id")]
    public string id
    {
        get => InternalId.ToString();
        set
        {
            _rawJsonId = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                Guid parsed = ParseId(value);
                if (parsed != Guid.Empty)
                    InternalId = parsed;
            }
        }
    }

    // Ensure that after deserialization completes, InternalId is populated if possible
    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        if (InternalId == Guid.Empty && !string.IsNullOrWhiteSpace(_rawJsonId))
        {
            Guid parsed = ParseId(_rawJsonId);
            if (parsed != Guid.Empty)
                InternalId = parsed;
        }
    }

    // System.Text.Json deserialization hook
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    // Handle System.Text.Json deserialization by checking extension data for 'id' field
    internal void ProcessExtensionData()
    {
        if (ExtensionData != null && ExtensionData.TryGetValue("id", out JsonElement idElement) && InternalId == Guid.Empty)
        {
            try
            {
                string? idValue = idElement.GetString();
                if (!string.IsNullOrWhiteSpace(idValue))
                {
                    Guid parsed = ParseId(idValue);
                    if (parsed != Guid.Empty)
                        InternalId = parsed;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }
    }

    // Always include partition key field used by the container path
    [JsonPropertyName("pk")]
    public string pk => PartitionKeyValue;

    // Type discriminator for the remaining authority record types.
    [JsonPropertyName("$type")]
    public string JsonType => TypeValue;

    // Internal properties (not serialized directly)
    [JsonIgnore]
    public string TypeValue { get; internal set; }

    [JsonIgnore]
    public string PartitionKeyValue { get; internal set; }

    // Methods to get configured property names (kept for SQL queries and callers)
    public static string GetTypePropertyName() => "$type";
    public static string GetPartitionKeyPropertyName() => "/pk";

    // Method to get the partition key path for Cosmos container configuration
    public static string GetPartitionKeyPath() => "/pk";

    // Get the JSON property name for PartitionKey (without the leading slash)
    public static string GetPartitionKeyJsonPropertyName() => GetPartitionKeyPath().TrimStart('/');

    public static string GetEffectiveTypeValue(string logicalType) => logicalType;

    /// <summary>
    /// Formats the ID value for the lowercase 'id' field when saving.
    /// </summary>
    public static string FormatIdForSave(Guid id, string type) => id.ToString();

    /// <summary>
    /// Parses the raw GUID id.
    /// </summary>
    public static Guid ParseId(string formattedId)
    {
        if (string.IsNullOrEmpty(formattedId))
            return Guid.Empty;

        if (Guid.TryParse(formattedId, out Guid directGuid))
            return directGuid;

        return Guid.Empty;
    }

    /// <summary>
    /// Gets the formatted ID value for this record (reading scenario)
    /// </summary>
    public string GetFormattedId() => FormatIdForSave(InternalId, TypeValue);

    /// <summary>
    /// Gets the internal ID value as a Guid
    /// </summary>
    public Guid GetId() => InternalId;

    /// <summary>
    /// Sets the internal ID value
    /// </summary>
    public void SetId(Guid id) => InternalId = id;
}
