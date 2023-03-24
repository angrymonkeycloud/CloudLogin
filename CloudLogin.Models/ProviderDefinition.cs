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
    public bool HandlesEmailAddress { get; init; } = false; // Should Be private
    public bool HandlesPhoneNumber { get; set; } = false; // Should Be private
    public bool IsCodeVerification { get; init; } = false; // Should Be private
    public bool HandleUpdateOnly { get; set; }

    public string CssClass // Should Be private
    {
        get
        {
            List<string> classes = new()
            {
                $"_{Code.ToLower()}"
            };

            return string.Join(" ", classes);
        }
    }
}
