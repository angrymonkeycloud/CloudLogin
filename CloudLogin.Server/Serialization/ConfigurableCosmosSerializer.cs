using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using System.IO;

namespace AngryMonkey.CloudLogin.Server.Serialization;

public class ConfigurableCosmosSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ConfigurableCosmosSerializer() => _jsonSerializerOptions = CreateDefaultOptions();

    public ConfigurableCosmosSerializer(JsonSerializerOptions? jsonSerializerOptions)
    {
        _jsonSerializerOptions = jsonSerializerOptions ?? CreateDefaultOptions();

        // Ensure our converter is included
        if (!_jsonSerializerOptions.Converters.Any(c => c is BaseRecordJsonConverter))
            _jsonSerializerOptions.Converters.Add(new BaseRecordJsonConverter());

        AddUtcConverters(_jsonSerializerOptions);
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = null, // Keep original property names (PascalCase)
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.Converters.Add(new BaseRecordJsonConverter());
        AddUtcConverters(options);

        return options;
    }

    /// <summary>
    /// Every persisted instant is written in UTC; only the display layer converts to the viewer's
    /// timezone. Applied at the serializer so no property can bypass it.
    /// </summary>
    private static void AddUtcConverters(JsonSerializerOptions options)
    {
        if (!options.Converters.Any(converter => converter is UtcDateTimeOffsetConverter))
            options.Converters.Add(new UtcDateTimeOffsetConverter());

        if (!options.Converters.Any(converter => converter is NullableUtcDateTimeOffsetConverter))
            options.Converters.Add(new NullableUtcDateTimeOffsetConverter());
    }

    public override T FromStream<T>(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
                return default!;

            if (typeof(Stream).IsAssignableFrom(typeof(T)))
                return (T)(object)stream;

            return JsonSerializer.Deserialize<T>(stream, _jsonSerializerOptions)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        MemoryStream memoryStream = new();
        JsonSerializer.Serialize(memoryStream, input, _jsonSerializerOptions);
        memoryStream.Position = 0;

        return memoryStream;
    }
}