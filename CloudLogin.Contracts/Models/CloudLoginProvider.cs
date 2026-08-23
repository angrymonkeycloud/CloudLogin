namespace AngryMonkey.CloudLogin;

public record CloudLoginProvider
{
    public required string Code { get; set; }
    public string? PasswordHash { get; set; }
    public string? Identifier { get; set; }
}
