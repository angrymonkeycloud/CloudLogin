namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginPasskeySummaryModel
{
    public string CredentialId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? LastUsedOn { get; set; }
    public bool IsBackedUp { get; set; }
}

public static class CloudLoginPasskeySummaryModelExtensions
{
    public static CloudLoginPasskeySummaryModel ToModel(this CloudLoginPasskeySummary source) => new()
    {
        CredentialId = source.CredentialId,
        Name = source.Name,
        CreatedOn = source.CreatedOn,
        LastUsedOn = source.LastUsedOn,
        IsBackedUp = source.IsBackedUp
    };
}
