using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Serialization;

/// <summary>
/// System.Text.Json converter for the fixed CloudLogin authority record shape.
/// </summary>
public class BaseRecordSystemTextJsonConverter<T> : JsonConverter<T> where T : CloudLoginBaseRecord
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        // Create an instance using the default constructor
        T? instance = (T?)Activator.CreateInstance(typeToConvert);
        if (instance == null)
            return null;

        // Parse standard properties
        if (root.TryGetProperty("id", out JsonElement idElement))
        {
            string? idValue = idElement.GetString();
            if (!string.IsNullOrEmpty(idValue))
                instance.id = idValue;
        }

        // Parse type/discriminator properties
        string? typeValue = null;
        if (root.TryGetProperty(CloudLoginBaseRecord.GetTypePropertyName(), out JsonElement typeElement))
            typeValue = typeElement.GetString();

        if (!string.IsNullOrEmpty(typeValue))
            instance.TypeValue = typeValue;

        // Parse partition key properties
        string? partitionKeyValue = null;
        if (root.TryGetProperty(CloudLoginBaseRecord.GetPartitionKeyJsonPropertyName(), out JsonElement pkElement))
            partitionKeyValue = pkElement.GetString();

        if (!string.IsNullOrEmpty(partitionKeyValue))
            instance.PartitionKeyValue = partitionKeyValue;

        // Use reflection to set other properties
        foreach (PropertyInfo property in typeToConvert.GetProperties())
        {
            if (ShouldSkipProperty(property))
                continue;

            JsonPropertyNameAttribute? jsonPropertyAttr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            string propertyName = jsonPropertyAttr?.Name ?? property.Name;

            if (root.TryGetProperty(propertyName, out JsonElement propertyElement))
            {
                try
                {
                    object? value = JsonSerializer.Deserialize(propertyElement.GetRawText(), property.PropertyType, options);
                    property.SetValue(instance, value);
                }
                catch
                {
                    // Skip properties that can't be deserialized
                }
            }
        }

        return instance;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Always write standard properties
        writer.WriteString("id", value.id);
        writer.WriteString("pk", value.pk);
        writer.WriteString(CloudLoginBaseRecord.GetTypePropertyName(), value.TypeValue);

        // Write other properties using reflection
        Type objectType = value.GetType();
        foreach (PropertyInfo property in objectType.GetProperties())
        {
            if (ShouldSkipProperty(property))
                continue;

            JsonPropertyNameAttribute? jsonPropertyAttr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            string propertyName = jsonPropertyAttr?.Name ?? property.Name;

            // Skip properties we've already handled
            if (IsStandardProperty(propertyName))
                continue;

            try
            {
                object? propertyValue = property.GetValue(value);
                if (propertyValue != null)
                {
                    writer.WritePropertyName(propertyName);
                    JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, options);
                }
                else if (!property.GetCustomAttributes<JsonIgnoreAttribute>().Any(attr => 
                    attr.Condition == JsonIgnoreCondition.WhenWritingNull))
                {
                    writer.WriteNull(propertyName);
                }
            }
            catch
            {
                // Skip properties that can't be serialized
            }
        }

        writer.WriteEndObject();
    }

    private static bool ShouldSkipProperty(PropertyInfo property)
    {
        // Skip properties with JsonIgnore attribute
        JsonIgnoreAttribute? ignoreAttr = property.GetCustomAttribute<JsonIgnoreAttribute>();
        if (ignoreAttr != null)
            return true;

        // Skip internal CloudLoginBaseRecord properties that shouldn't be serialized directly
        return property.Name == nameof(CloudLoginBaseRecord.TypeValue) ||
               property.Name == nameof(CloudLoginBaseRecord.PartitionKeyValue) ||
               property.Name == nameof(CloudLoginBaseRecord.InternalId) ||
               property.Name == nameof(CloudLoginBaseRecord.ExtensionData);
    }

    private static bool IsStandardProperty(string propertyName)
    {
        return propertyName == "id" ||
               propertyName == "pk" ||
               propertyName == CloudLoginBaseRecord.GetTypePropertyName() ||
               propertyName == CloudLoginBaseRecord.GetPartitionKeyJsonPropertyName() ||
               propertyName == "ID" ||
               propertyName == "PartitionKey" ||
               propertyName == "Discriminator";
    }
}
