using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AngryMonkey.CloudLogin.DataContract;

[JsonConverter(typeof(StringEnumConverter))]
public enum InputFormat
{
    EmailAddress,
    PhoneNumber,
    Other
}
