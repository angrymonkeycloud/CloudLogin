
namespace AngryMonkey.CloudLogin;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum InputFormat
{
    EmailAddress,
    PhoneNumber,
    Other
}