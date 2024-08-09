namespace AngryMonkey.CloudLogin.Data;
public record Request : BaseRecord
{
    public Request() : base("Request", "Request") { }
    public Guid? UserId { get; set; }
    public int ttl { get; set; } = 60;
}