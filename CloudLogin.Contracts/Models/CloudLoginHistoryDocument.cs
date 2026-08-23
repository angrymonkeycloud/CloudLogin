namespace AngryMonkey.CloudLogin;

/// <summary>
/// The per-user login-history document. Persisted as a single JSON blob per user rather than
/// on the user record, so an active account's sign-in log never bloats the document that gets
/// read on every request.
/// </summary>
public record CloudLoginHistoryDocument
{
    public Guid UserId { get; set; }
    public List<CloudLoginHistoryEntry> Entries { get; set; } = [];
}
