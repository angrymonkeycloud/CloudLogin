namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginProviderDefinitionModel
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HandleUpdateOnly { get; set; }
    public bool HandlesEmailAddress { get; set; }
    public bool HandlesPhoneNumber { get; set; }
    public bool IsCodeVerification { get; set; }
    public bool InputRequired { get; set; }
    public bool IsExternal { get; set; }

    public string CssClass => $"_{Code.ToLowerInvariant()}";
}

public static class CloudLoginProviderDefinitionModelExtensions
{
    public static CloudLoginProviderDefinitionModel ToModel(this CloudLoginProviderDefinition source) => new()
    {
        Code = source.Code,
        Label = source.Label,
        HandleUpdateOnly = source.HandleUpdateOnly,
        HandlesEmailAddress = source.HandlesEmailAddress,
        HandlesPhoneNumber = source.HandlesPhoneNumber,
        IsCodeVerification = source.IsCodeVerification,
        InputRequired = source.InputRequired,
        IsExternal = source.IsExternal
    };
}
