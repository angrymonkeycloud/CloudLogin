using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using System.IO;

namespace AngryMonkey.CloudLogin.Server.Serialization;

public class ConfigurableCosmosSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ConfigurableCosmosSerializer()
    {
        _jsonSerializerOptions = CreateDefaultOptions();
    }

    public ConfigurableCosmosSerializer(JsonSerializerOptions? jsonSerializerOptions)
    {
        _jsonSerializerOptions = jsonSerializerOptions ?? CreateDefaultOptions();
        
        // Ensure our converter is included
        if (!_jsonSerializerOptions.Converters.Any(c => c is BaseRecordJsonConverter))
        {
            _jsonSerializerOptions.Converters.Add(new BaseRecordJsonConverter());
        }
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // Keep original property names (PascalCase)
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        
        options.Converters.Add(new BaseRecordJsonConverter());
        return options;
    }

    public override T FromStream<T>(Stream stream)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
            {
                return default!;
            }

            if (typeof(Stream).IsAssignableFrom(typeof(T)))
            {
                return (T)(object)stream;
            }

            return JsonSerializer.Deserialize<T>(stream, _jsonSerializerOptions)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var memoryStream = new MemoryStream();
        JsonSerializer.Serialize(memoryStream, input, _jsonSerializerOptions);
        memoryStream.Position = 0;
        return memoryStream;
    }
}