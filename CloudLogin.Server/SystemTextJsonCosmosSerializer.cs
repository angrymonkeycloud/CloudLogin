using System.Text.Json;
using Microsoft.Azure.Cosmos;

public class SystemTextJsonCosmosSerializer(JsonSerializerOptions serializerOptions) : CosmosSerializer
{
    private readonly JsonSerializerOptions _serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));

    public override T FromStream<T>(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
            return (T)(object)stream;

        using (stream)
        {
            return JsonSerializer.Deserialize<T>(stream, _serializerOptions)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        MemoryStream streamPayload = new();
        JsonSerializer.Serialize(streamPayload, input, _serializerOptions);
        streamPayload.Position = 0;
        return streamPayload;
    }
}
