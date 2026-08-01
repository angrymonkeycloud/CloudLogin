using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server;

public abstract record BaseRecord
{
    // Static configuration for dynamic property naming
    public static CosmosConfiguration? CosmosConfiguration { get; set; }

    internal BaseRecord(string partitionKey, string type)
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

    // Cosmos DB requires lowercase 'id' property. How it's saved depends on configuration.
    // Setter parses either a raw GUID or "{type}|{guid}" and assigns to InternalId.
    [JsonPropertyName("id")]
    public string id
    {
        get => FormatIdForSave(InternalId, TypeValue);
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

    // Also include legacy partition key name for backward compatibility when configured
    [JsonPropertyName("PartitionKey")]
    public string? LegacyPartitionKey => ShouldIncludeLegacySchema() ? PartitionKeyValue : null;

    // Include both modern and legacy type discriminator names so queries work in both modes
    [JsonPropertyName("$type")]
    public string JsonType => TypeValue;

    [JsonPropertyName("Discriminator")]
    public string? LegacyDiscriminator => ShouldIncludeLegacySchema() ? TypeValue : null;

    // Internal properties (not serialized directly)
    [JsonIgnore]
    public string TypeValue { get; internal set; }

    [JsonIgnore]
    public string PartitionKeyValue { get; internal set; }

    // Methods to get configured property names (kept for SQL queries and callers)
    public static string GetTypePropertyName() => CosmosConfiguration?.TypeName ?? "$type";
    public static string GetPartitionKeyPropertyName() => CosmosConfiguration?.PartitionKeyName ?? "/pk";

    // Method to get the partition key path for Cosmos container configuration
    public static string GetPartitionKeyPath() => CosmosConfiguration?.PartitionKeyName ?? "/pk";

    // Get the JSON property name for PartitionKey (without the leading slash)
    public static string GetPartitionKeyJsonPropertyName() => GetPartitionKeyPath().TrimStart('/');

    public static bool ShouldIncludeLegacySchema() => CosmosConfiguration?.IncludeLegacySchema ?? false;

    // Get the JSON compatibility mode
    public static JsonCompatibilityMode GetJsonCompatibilityMode() => CosmosConfiguration?.JsonCompatibilityMode ?? JsonCompatibilityMode.Standard;

    // Get the ID save mode configuration
    public static IdSaveMode GetIdSaveMode() => CosmosConfiguration?.SaveIdMode ?? IdSaveMode.Raw;

    // Resolve the effective discriminator/partition value for a logical record type name
    public static string GetEffectiveTypeValue(string logicalType)
    {
        if (!string.Equals(logicalType, nameof(UserInfo), StringComparison.Ordinal))
            return logicalType;

        string? overrideValue = CosmosConfiguration?.UserInfoPartitionKeyValue;
        return string.IsNullOrWhiteSpace(overrideValue) ? logicalType : overrideValue;
    }

    /// <summary>
    /// Formats the ID value for the lowercase 'id' field when saving.
    /// </summary>
    public static string FormatIdForSave(Guid id, string type) => GetIdSaveMode() switch
    {
        IdSaveMode.TypePrefixed => $"{type}|{id}",
        _ => id.ToString()
    };

    /// <summary>
    /// Extracts the GUID from a possibly type-prefixed ID string (reading scenario)
    /// </summary>
    public static Guid ParseId(string formattedId)
    {
        if (string.IsNullOrEmpty(formattedId))
            return Guid.Empty;

        string separator = "|";
        int separatorIndex = formattedId.IndexOf(separator, StringComparison.Ordinal);

        if (separatorIndex > 0 && separatorIndex < formattedId.Length - 1)
        {
            string guidPart = formattedId[(separatorIndex + separator.Length)..];
            if (Guid.TryParse(guidPart, out Guid guid))
                return guid;
        }

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
