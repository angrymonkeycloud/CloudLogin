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

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        // Get configured property names (keep full names)
        var typePropertyName = BaseRecord.GetTypePropertyName();
        var partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();

        // First, determine the document type
        var typeValue = GetPropertyValue(root, typePropertyName, "type", "$type");
        if (string.IsNullOrEmpty(typeValue))
            throw new JsonException("Cannot determine document type - type property is missing");

        // Create the appropriate concrete type
        BaseRecord? instance = typeValue switch
        {
            "UserInfo" => new UserInfo(),
            "Request" => new LoginRequest(),
            _ => throw new JsonException($"Unknown BaseRecord type: {typeValue}")
        };

        // Set the basic properties
        SetPropertyFromJson(root, instance, "id", nameof(BaseRecord.ID));
        SetPropertyFromJson(root, instance, typePropertyName, nameof(BaseRecord.Type));
        SetPropertyFromJson(root, instance, partitionKeyPropertyName, nameof(BaseRecord.PartitionKey));

        // Handle additional properties for the specific type
        DeserializeAdditionalProperties(root, instance, options);

        return instance;
    }

    private static string? GetPropertyValue(JsonElement root, params string[] possibleNames)
    {
        foreach (var name in possibleNames)
        {
            if (TryGetProperty(root, name, out var value))
                return value;
        }
        return null;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        
        // Try exact match
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }
        
        // Try case-insensitive search
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    value = prop.Value.GetString();
                    return true;
                }
            }
        }
        
        return false;
    }

    private static void SetPropertyFromJson(JsonElement root, BaseRecord instance, string jsonPropertyName, string objectPropertyName)
    {
        if (TryGetProperty(root, jsonPropertyName, out var value) && !string.IsNullOrEmpty(value))
        {
            var property = typeof(BaseRecord).GetProperty(objectPropertyName);
            if (property != null && property.CanWrite)
            {
                if (property.PropertyType == typeof(Guid))
                {
                    if (Guid.TryParse(value, out var guidValue))
                        property.SetValue(instance, guidValue);
                }
                else if (property.PropertyType == typeof(string))
                {
                    property.SetValue(instance, value);
                }
            }
        }
    }

    private static void DeserializeAdditionalProperties(JsonElement root, BaseRecord instance, JsonSerializerOptions options)
    {
        var typePropertyName = BaseRecord.GetTypePropertyName();
        var partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        var excludedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", typePropertyName, partitionKeyPropertyName, "type", "$type", "pk"
        };

        foreach (var property in root.EnumerateObject())
        {
            if (excludedProperties.Contains(property.Name))
                continue;

            var propInfo = instance.GetType().GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propInfo != null && propInfo.CanWrite)
            {
                try
                {
                    var value = JsonSerializer.Deserialize(property.Value.GetRawText(), propInfo.PropertyType, options);
                    propInfo.SetValue(instance, value);
                }
                catch
                {
                    // Skip properties that can't be deserialized
                }
            }
        }
    }

    public override void Write(Utf8JsonWriter writer, BaseRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Write ID
        writer.WriteString("id", value.ID.ToString());

        // Write Type with configured property name (don't strip $ or /)
        var typePropertyName = BaseRecord.GetTypePropertyName();
        writer.WriteString(typePropertyName, value.Type);

        // Write PartitionKey with configured property name
        var partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        writer.WriteString(partitionKeyPropertyName, value.PartitionKey);

        // Write other properties
        var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(BaseRecord.ID) && 
                       p.Name != nameof(BaseRecord.Type) && 
                       p.Name != nameof(BaseRecord.PartitionKey) &&
                       p.CanRead &&
                       !p.GetCustomAttributes<JsonIgnoreAttribute>().Any());

        foreach (var property in properties)
        {
            var propertyValue = property.GetValue(value);
            if (propertyValue != null)
            {
                var propertyName = GetJsonPropertyName(property);
                writer.WritePropertyName(propertyName);
                JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, options);
            }
        }

        writer.WriteEndObject();
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        // Check for JsonPropertyName attribute
        var jsonPropertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (jsonPropertyName != null)
            return jsonPropertyName.Name;

        // Keep original property name (PascalCase) - don't convert to camelCase
        return property.Name;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(BaseRecord).IsAssignableFrom(typeToConvert);
    }
}