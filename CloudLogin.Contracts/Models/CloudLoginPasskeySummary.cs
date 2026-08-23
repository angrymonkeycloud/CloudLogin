namespace AngryMonkey.CloudLogin;

/// <summary>Display-safe view of a registered passkey.</summary>
public record CloudLoginPasskeySummary
{
    public string CredentialId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? LastUsedOn { get; set; }
    public bool IsBackedUp { get; set; }
}
