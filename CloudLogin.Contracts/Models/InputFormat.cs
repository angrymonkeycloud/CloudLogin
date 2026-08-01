
namespace AngryMonkey.CloudLogin;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum InputFormat
{
    EmailAddress,
    PhoneNumber,
    Other
}