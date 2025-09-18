using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;

namespace AngryMonkey.CloudLogin.Server.Serialization;

public class BaseRecordJsonConverter : JsonConverter<BaseRecord>
{
    public override BaseRecord? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        // Get configured property names
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();

        // First, determine the document type
        string? typeValue = GetPropertyValue(root, typePropertyName, "type", "$type", "Discriminator");
        if (string.IsNullOrEmpty(typeValue))
            throw new JsonException("Cannot determine document type - type property is missing");

        // Create the appropriate concrete type
        BaseRecord? instance = typeValue switch
        {
            "UserInfo" or "User" => new UserInfo(),
            "Request" => new LoginRequest(),
            _ => throw new JsonException($"Unknown BaseRecord type: {typeValue}")
        };

        // Set the basic properties - try multiple possible property names
        SetIdFromJson(root, instance);
        SetPropertyFromJson(root, instance, typePropertyName, nameof(BaseRecord.TypeValue));
        SetPropertyFromJson(root, instance, partitionKeyPropertyName, nameof(BaseRecord.PartitionKeyValue));

        // Handle additional properties for the specific type
        DeserializeAdditionalProperties(root, instance, options);

        return instance;
    }

    private static string? GetPropertyValue(JsonElement root, params string[] possibleNames)
    {
        foreach (string name in possibleNames)
            if (TryGetProperty(root, name, out string? value))
                return value;

        return null;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        
        // Try exact match
        if (root.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }
        
        // Try case-insensitive search
        foreach (JsonProperty prop in root.EnumerateObject())
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    value = prop.Value.GetString();
                    return true;
                }
        
        return false;
    }

    private static void SetIdFromJson(JsonElement root, BaseRecord instance)
    {
        // Try to get ID from various possible property names
        string? idValue = GetPropertyValue(root, "id", "ID");
        if (!string.IsNullOrEmpty(idValue))
        {
            Guid parsedId = BaseRecord.ParseId(idValue);
            if (parsedId != Guid.Empty)
                instance.SetId(parsedId);
        }
    }

    private static void SetPropertyFromJson(JsonElement root, BaseRecord instance, string jsonPropertyName, string objectPropertyName)
    {
        if (TryGetProperty(root, jsonPropertyName, out string? value) && !string.IsNullOrEmpty(value))
        {
            PropertyInfo? property = typeof(BaseRecord).GetProperty(objectPropertyName);
            
            if (property != null && property.CanWrite)
            {
                if (property.PropertyType == typeof(Guid))
                {
                    if (Guid.TryParse(value, out Guid guidValue))
                        property.SetValue(instance, guidValue);
                }
                else if (property.PropertyType == typeof(string))
                    property.SetValue(instance, value);
            }
        }
    }

    private static void DeserializeAdditionalProperties(JsonElement root, BaseRecord instance, JsonSerializerOptions options)
    {
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();

        HashSet<string> excludedProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "id", "ID", typePropertyName, partitionKeyPropertyName, 
            "type", "$type", "Discriminator", "pk", "PartitionKey"
        };

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (excludedProperties.Contains(property.Name))
                continue;

            PropertyInfo? propInfo = instance.GetType().GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propInfo != null && propInfo.CanWrite)
                try
                {
                    object? value = JsonSerializer.Deserialize(property.Value.GetRawText(), propInfo.PropertyType, options);
                    propInfo.SetValue(instance, value);
                }
                catch
                {
                    // Skip properties that can't be deserialized
                }
        }
    }

    public override void Write(Utf8JsonWriter writer, BaseRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        JsonCompatibilityMode compatibilityMode = BaseRecord.GetJsonCompatibilityMode();
        string typePropertyName = BaseRecord.GetTypePropertyName();
        string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();

        // Write the lowercase 'id' field using the BaseRecord's getter
        writer.WriteString("id", value.id);

        // Write uppercase 'ID' field only if legacy schema is enabled
        if (BaseRecord.ShouldIncludeLegacySchema())
            writer.WriteString("ID", value.GetId().ToString());

        // Write Type with configured property name
        writer.WriteString(typePropertyName, value.TypeValue);

        // Write PartitionKey with configured property name
        writer.WriteString(partitionKeyPropertyName, value.PartitionKeyValue);

        // In Legacy mode, also write the old property names for backward compatibility
        if (compatibilityMode == JsonCompatibilityMode.Legacy)
        {
            if (typePropertyName != "$type")
                writer.WriteString("$type", value.TypeValue);
            
            if (partitionKeyPropertyName != "pk")
                writer.WriteString("pk", value.PartitionKeyValue);
        }

        // Write other properties
        IEnumerable<PropertyInfo> properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "InternalId" && 
                       p.Name != nameof(BaseRecord.TypeValue) && 
                       p.Name != nameof(BaseRecord.PartitionKeyValue) &&
                       p.Name != "id" &&
                       p.CanRead &&
                       !p.GetCustomAttributes<JsonIgnoreAttribute>().Any());

        foreach (PropertyInfo? property in properties)
        {
            object? propertyValue = property.GetValue(value);
            
            if (propertyValue != null)
            {
                string propertyName = GetJsonPropertyName(property);
                writer.WritePropertyName(propertyName);
                JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, options);
            }
        }

        writer.WriteEndObject();
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        // Check for JsonPropertyName attribute
        JsonPropertyNameAttribute? jsonPropertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        
        if (jsonPropertyName != null)
            return jsonPropertyName.Name;

        // Keep original property name (PascalCase) - don't convert to camelCase
        return property.Name;
    }

    public override bool CanConvert(Type typeToConvert) => typeof(BaseRecord).IsAssignableFrom(typeToConvert);
}