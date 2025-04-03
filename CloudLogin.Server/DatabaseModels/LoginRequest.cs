namespace AngryMonkey.CloudLogin.Server;
public record LoginRequest : BaseRecord
{
    public LoginRequest() : base("Request", "Request") { }
    public Guid? UserId { get; set; }
    public int ttl { get; set; } = 60;
}