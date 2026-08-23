namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginConnectedProviderModel
{
    public string Code { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string Input { get; set; } = string.Empty;
    public bool CanDisconnect { get; set; }
}

public static class CloudLoginConnectedProviderModelExtensions
{
    public static CloudLoginConnectedProviderModel ToModel(this CloudLoginConnectedProvider source) => new()
    {
        Code = source.Code,
        Label = source.Label,
        Input = source.Input,
        CanDisconnect = source.CanDisconnect
    };
}
