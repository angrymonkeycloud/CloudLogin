using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AngryMonkey.Cloud.Login.DataContract;

[JsonConverter(typeof(StringEnumConverter))]
public enum InputFormat
{
	EmailAddress,
	PhoneNumber,
	Other
}
