namespace AngryMonkey.Cloud.Login.DataContract;
public record CloudRequest : BaseRecord
{
    public CloudRequest() : base("CloudRequest", "CloudRequest") { }
    public Guid? UserId { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}