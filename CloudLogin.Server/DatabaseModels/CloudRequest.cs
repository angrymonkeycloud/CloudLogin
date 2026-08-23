namespace AngryMonkey.CloudLogin.Server;
public record CloudRequest : CloudLoginBaseRecord
{
    public CloudRequest() : base("Request", "Request") { }
    public Guid? UserId { get; set; }
    public int ttl { get; set; } = 60;
}
