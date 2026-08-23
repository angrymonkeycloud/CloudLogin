namespace AngryMonkey.CloudLogin;

public sealed class CloudLoginWebhookRegistration
{
    public required string Application { get; set; }
    public required Uri Url { get; set; }
    public required string Secret { get; set; }
    public HashSet<string> Events { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
