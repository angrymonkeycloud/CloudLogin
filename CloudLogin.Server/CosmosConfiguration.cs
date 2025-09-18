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
        
        // New: Include legacy fields alongside the modern schema
        IncludeLegacySchema = configurationSection.GetValue("IncludeLegacySchema", false)
                                || configurationSection.GetValue("UseLegacySchema", false); // backward compat with old key
        
        // New: control how the lowercase 'id' field is saved
        var saveIdModeStr = configurationSection["SaveIdMode"] ?? configurationSection["IdFormat"]; // backward compat with IdFormat
        if (!Enum.TryParse(saveIdModeStr, ignoreCase: true, out IdSaveMode saveMode))
            saveMode = IdSaveMode.Raw;
        SaveIdMode = saveMode;

        // Property name customization
        PartitionKeyName = configurationSection["PartitionKeyName"] ?? "/pk";
        TypeName = configurationSection["TypeName"] ?? "$type";

        // Optional override for logical UserInfo discriminator/partition key value
        UserInfoPartitionKeyValue = configurationSection["UserInfoPartitionKeyValue"];

        // Compatibility flags
        UseUppercaseIdProperty = configurationSection.GetValue("UseUppercaseIdProperty", false) || IncludeLegacySchema;
        JsonCompatibilityMode = Enum.TryParse(
            configurationSection["JsonCompatibilityMode"], 
            out JsonCompatibilityMode mode) ? mode : JsonCompatibilityMode.Standard;
    }

    public CosmosConfiguration() { }

    public string? AspireName { get; set; }
    public string? ConnectionString { get; set; }
    public string? DatabaseId { get; set; }
    public string? ContainerId { get; set; }
    
    /// <summary>
    /// Include legacy fields/properties alongside the modern schema (default: false)
    /// When true: also emits legacy property names such as "PartitionKey", "Discriminator", and uppercase "ID".
    /// </summary>
    public bool IncludeLegacySchema { get; set; } = false;

    /// <summary>
    /// Controls how the lowercase 'id' field is saved. Raw => just the GUID; TypePrefixed => "{type}|{guid}".
    /// This affects writing only. Reading will handle both.
    /// </summary>
    public IdSaveMode SaveIdMode { get; set; } = IdSaveMode.Raw;
    
    public string PartitionKeyName { get; set; } = "/pk";
    public string TypeName { get; set; } = "$type";

    /// <summary>
    /// Optional override for the logical UserInfo discriminator/partition key value.
    /// If set (e.g. "User"), new records of type UserInfo will use this value for pk and $type/Discriminator.
    /// </summary>
    public string? UserInfoPartitionKeyValue { get; set; }
        
    /// <summary>
    /// Whether to also include an uppercase "ID" property in JSON (default: false)
    /// </summary>
    public bool UseUppercaseIdProperty { get; set; } = false;
    
    /// <summary>
    /// Determines how the JSON should be structured for compatibility
    /// </summary>
    public JsonCompatibilityMode JsonCompatibilityMode { get; set; } = JsonCompatibilityMode.Standard;
    
    public bool IsValid() => !string.IsNullOrEmpty(AspireName) || !string.IsNullOrEmpty(ConnectionString);
}

/**
 * Defines different JSON compatibility modes for serialization
 */
public enum JsonCompatibilityMode
{
    /// <summary>
    /// Standard mode - uses configured property names as-is
    /// </summary>
    Standard,
    
    /// <summary>
    /// Legacy mode - includes both old and new property names for backward compatibility
    /// </summary>
    Legacy,
    
    /// <summary>
    /// Custom mode - allows full customization of all property names
    /// </summary>
    Custom
}

/// <summary>
/// Controls how the lowercase 'id' field is saved in Cosmos documents.
/// Raw => just the GUID; TypePrefixed => "{type}|{guid}".
/// </summary>
public enum IdSaveMode
{
    Raw,
    TypePrefixed
}
