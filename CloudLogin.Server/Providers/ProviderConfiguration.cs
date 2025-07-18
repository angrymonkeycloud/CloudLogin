namespace AngryMonkey.CloudLogin.Sever.Providers;

public abstract class ProviderConfiguration
{
    internal ProviderConfiguration() { }

    internal void Init(string code, string? label = null)
    {
        Code = code;
        Label = label ?? Code;
    }

    public string Code { get; private set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HandlesEmailAddress { get; init; } = false; // Should Be private
    public bool HandlesPhoneNumber { get; set; } = false; // Should Be private
    public bool IsCodeVerification { get; init; } = false; // Should Be private
    public bool InputRequired { get; init; } = false; // Should Be private
    public bool HandleUpdateOnly { get; set; }

    // Should Be private
    public string CssClass => string.Join(" ", [$"_{Code.ToLower()}"]);
}
