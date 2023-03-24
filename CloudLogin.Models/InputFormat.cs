using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AngryMonkey.CloudLogin;

[JsonConverter(typeof(StringEnumConverter))]
public enum InputFormat
{
    EmailAddress,
    PhoneNumber,
    Other
}
