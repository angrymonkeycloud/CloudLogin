namespace AngryMonkey.CloudLogin;

public record LoginProvider
{
    public required string Code { get; set; }
    public string? PasswordHash { get; set; }
    public string? Identifier { get; set; }
}
