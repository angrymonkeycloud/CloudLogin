namespace AngryMonkey.CloudLogin.Services;

public class EmailServiceOptions
{
    public required string FromEmail { get; set; }
    public required string BccEmail { get; set; }
    public required string ClientId { get; set; }
    public required string TenantId { get; set; }
    public required string Secret { get; set; }
}
