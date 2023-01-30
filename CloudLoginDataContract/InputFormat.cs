using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CloudLoginDataContract;

[JsonConverter(typeof(StringEnumConverter))]
public enum InputFormat
{
    EmailAddress,
    PhoneNumber,
    Other
}
