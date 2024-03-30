using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputFormat
{
    EmailAddress,
    PhoneNumber,
    Other
}
