using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

public class ProviderDefinition
{
    public ProviderDefinition(string code, bool handleUpdateOnly = false, string? label = null)
    {
        Code = code;
        Label = label ?? Code;
        HandleUpdateOnly = handleUpdateOnly;
    }

    public string Code { get; init; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HandleUpdateOnly { get; set; }

    public required bool HandlesEmailAddress { get; set; }
    public required bool HandlesPhoneNumber { get; set; }
    public required bool IsCodeVerification { get; set; }
    public required bool InputRequired { get; set; }
    public required bool IsExternal { get; set; }

    [JsonIgnore]
    public string CssClass // Should Be private
    {
        get
        {
            List<string> classes = [$"_{Code.ToLowerInvariant()}"];

            return string.Join(" ", classes);
        }
    }
}
