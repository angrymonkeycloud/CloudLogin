namespace AngryMonkey.CloudLogin;
public record DbRequest : BaseRecord
{
    public DbRequest() : base("CloudRequest", "CloudRequest") { }
    public Guid? UserId { get; set; }
    public int ttl { get; set; } = 60;
}