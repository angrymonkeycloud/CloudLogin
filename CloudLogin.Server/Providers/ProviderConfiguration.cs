namespace AngryMonkey.CloudLogin.Sever.Providers;

public abstract class ProviderConfiguration
{
    internal ProviderConfiguration() { }

    internal void Init(string code, bool isExternal, string? label = null)
    {
        Code = code;
        IsExternal = isExternal;
        Label = label ?? Code;
    }

    public string Code { get; private set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HandlesEmailAddress { get; init; } = false; // Should Be private
    public bool HandlesPhoneNumber { get; set; } = false; // Should Be private
    public bool IsCodeVerification { get; init; } = false; // Should Be private
    public bool InputRequired { get; init; } = false; // Should Be private
    public bool HandleUpdateOnly { get; set; }
    public bool IsExternal { get; set; } = false;

    public bool DisplayAsButton => !InputRequired;

    // Should Be private
    public string CssClass => string.Join(" ", [$"_{Code.ToLower()}"]);

    public ProviderDefinition ToModel()
    {
        return new ProviderDefinition(Code, HandleUpdateOnly, Label)
        {
            HandlesEmailAddress = HandlesEmailAddress,
            HandlesPhoneNumber = HandlesPhoneNumber,
            IsCodeVerification = IsCodeVerification,
            InputRequired = InputRequired,
            IsExternal = IsExternal
        };
    }
}
