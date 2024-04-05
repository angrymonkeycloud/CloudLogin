namespace AngryMonkey.CloudLogin;

public record LoginProvider
{
    public string Code { get; set; } = string.Empty;
    public string? Identifier { get; set; }
}
