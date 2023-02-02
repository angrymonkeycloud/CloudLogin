namespace AngryMonkey.CloudLogin.DataContract;

public record LoginProvider
{
    public string Code { get; set; } = string.Empty;
    public string? Identifier { get; set; }
}
