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
    [Newtonsoft.Json.JsonIgnore]
    internal Guid InternalId { get; set; }

    // Keep the raw JSON id value to handle any deserialization ordering edge cases
    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    private string? _rawJsonId;

    // Cosmos DB requires lowercase 'id' property. How it's saved depends on configuration.
    // Setter parses either a raw GUID or "{type}|{guid}" and assigns to InternalId.
    [Newtonsoft.Json.JsonProperty("id")]
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
    [Newtonsoft.Json.JsonProperty("pk")]
    public string pk => PartitionKeyValue;

    // Also include legacy partition key name for backward compatibility when configured
    [JsonPropertyName("PartitionKey")]
    [Newtonsoft.Json.JsonProperty("PartitionKey")]
    public string? LegacyPartitionKey => ShouldIncludeLegacySchema() ? PartitionKeyValue : null;

    // Include both modern and legacy type discriminator names so queries work in both modes
    [JsonPropertyName("$type")]
    [Newtonsoft.Json.JsonProperty("$type")]
    public string JsonType => TypeValue;

    [JsonPropertyName("Discriminator")]
    [Newtonsoft.Json.JsonProperty("Discriminator")]
    public string? LegacyDiscriminator => ShouldIncludeLegacySchema() ? TypeValue : null;

    // Internal properties (not serialized directly)
    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string TypeValue { get; internal set; }

    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
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

/// <summary>
/// Custom contract resolver for BaseRecord serialization that handles dynamic property names
/// and guarantees Cosmos DB partition key compatibility.
/// </summary>
public class BaseRecordContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
{
    protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
    {
        IList<Newtonsoft.Json.Serialization.JsonProperty> properties = base.CreateProperties(type, memberSerialization);

        if (!typeof(BaseRecord).IsAssignableFrom(type))
            return properties;

        bool includeLegacy = BaseRecord.ShouldIncludeLegacySchema();

        // Add uppercase "ID" property only when legacy schema is enabled
        if (includeLegacy)
        {
            properties.Add(new Newtonsoft.Json.Serialization.JsonProperty
            {
                PropertyName = "ID",
                PropertyType = typeof(Guid),
                DeclaringType = type,
                Readable = true,
                Writable = true,
                ValueProvider = new IdValueProvider()
            });
        }

        // Hide the internal members from default serialization
        Newtonsoft.Json.Serialization.JsonProperty? typeValueProp = properties.FirstOrDefault(p => string.Equals(p.PropertyName, nameof(BaseRecord.TypeValue), StringComparison.Ordinal));
        if (typeValueProp != null)
            typeValueProp.Ignored = true;

        Newtonsoft.Json.Serialization.JsonProperty? pkValueProp = properties.FirstOrDefault(p => string.Equals(p.PropertyName, nameof(BaseRecord.PartitionKeyValue), StringComparison.Ordinal));
        if (pkValueProp != null)
            pkValueProp.Ignored = true;

        // Hide InternalId from serialization
        Newtonsoft.Json.Serialization.JsonProperty? internalIdProp = properties.FirstOrDefault(p => string.Equals(p.PropertyName, "InternalId", StringComparison.Ordinal));
        if (internalIdProp != null)
            internalIdProp.Ignored = true;

        // Hide ExtensionData from Newtonsoft.Json serialization
        Newtonsoft.Json.Serialization.JsonProperty? extensionDataProp = properties.FirstOrDefault(p => string.Equals(p.PropertyName, nameof(BaseRecord.ExtensionData), StringComparison.Ordinal));
        if (extensionDataProp != null)
            extensionDataProp.Ignored = true;

        // Handle legacy property visibility based on configuration
        if (!includeLegacy)
        {
            // Hide legacy properties when legacy schema is not enabled
            Newtonsoft.Json.Serialization.JsonProperty? legacyPkProp = properties.FirstOrDefault(p => string.Equals(p.PropertyName, nameof(BaseRecord.LegacyPartitionKey), StringComparison.Ordinal));
            if (legacyPkProp != null)
                legacyPkProp.Ignored = true;

            Newtonsoft.Json.Serialization.JsonProperty? legacyDiscProp = properties.FirstOrDefault(p => string.Equals(p.PropertyName, nameof(BaseRecord.LegacyDiscriminator), StringComparison.Ordinal));
            if (legacyDiscProp != null)
                legacyDiscProp.Ignored = true;
        }

        // Add configured type property alias if missing
        string configuredTypePropertyName = BaseRecord.GetTypePropertyName();

        if (!properties.Any(p => string.Equals(p.PropertyName, configuredTypePropertyName, StringComparison.Ordinal)))
            properties.Add(new Newtonsoft.Json.Serialization.JsonProperty
            {
                PropertyName = configuredTypePropertyName,
                PropertyType = typeof(string),
                DeclaringType = type,
                Readable = true,
                Writable = true,
                ValueProvider = new TypeValueProvider()
            });

        // Add configured partition key alias if missing
        string configuredPkJsonName = BaseRecord.GetPartitionKeyJsonPropertyName();

        if (!properties.Any(p => string.Equals(p.PropertyName, configuredPkJsonName, StringComparison.Ordinal)))
            properties.Add(new Newtonsoft.Json.Serialization.JsonProperty
            {
                PropertyName = configuredPkJsonName,
                PropertyType = typeof(string),
                DeclaringType = type,
                Readable = true,
                Writable = true,
                ValueProvider = new PartitionKeyValueProvider()
            });

        return properties;
    }
}

/// <summary>
/// Value provider for ID property
/// </summary>
public class IdValueProvider : Newtonsoft.Json.Serialization.IValueProvider
{
    public object? GetValue(object target) => target is BaseRecord record ? record.InternalId : null;

    public void SetValue(object target, object? value)
    {
        if (target is BaseRecord record && value is Guid guidValue)
            record.InternalId = guidValue;
        else if (target is BaseRecord record2 && value is string stringValue && Guid.TryParse(stringValue, out Guid parsedGuid))
            record2.InternalId = parsedGuid;
    }
}

/// <summary>
/// Value provider for TypeValue property
/// </summary>
public class TypeValueProvider : Newtonsoft.Json.Serialization.IValueProvider
{
    public object? GetValue(object target) => target is BaseRecord record ? record.TypeValue : null;

    public void SetValue(object target, object? value)
    {
        if (target is BaseRecord record && value is string stringValue)
            record.TypeValue = stringValue;
    }
}

/// <summary>
/// Value provider for PartitionKeyValue property
/// </summary>
public class PartitionKeyValueProvider : Newtonsoft.Json.Serialization.IValueProvider
{
    public object? GetValue(object target) => target is BaseRecord record ? record.PartitionKeyValue : null;

    public void SetValue(object target, object? value)
    {
        if (target is BaseRecord record && value is string stringValue)
            record.PartitionKeyValue = stringValue;
    }
}
