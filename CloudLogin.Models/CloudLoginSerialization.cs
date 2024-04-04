using System.Text.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

public class CloudLoginSerialization
{
    public static JsonSerializerOptions Options
    {
        get
        {
            JsonSerializerOptions jsonSerializerOptions = new()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = null
            };

            jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

            return jsonSerializerOptions;
        }
    }
}
