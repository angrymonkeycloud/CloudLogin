namespace AngryMonkey.CloudLogin;

/// <summary>
/// Composes the name shown for a user, from the two fields that are actually stored.
/// </summary>
/// <remarks>
/// <para>
/// A display name is derived, never held: it is the first and last name with a space between
/// them, computed wherever it is needed and stored nowhere. Keeping a third copy alongside the
/// two names it comes from only creates a value that can disagree with them - renaming someone
/// used to leave their old display name behind on every screen that preferred it, and the two
/// had no way back into agreement short of an edit to a field the person could not see the
/// purpose of.
/// </para>
/// <para>
/// One place composes it so every caller - the contract, the account UI, the API responses, and
/// the query that searches by it - agrees on what a given pair of names produces.
/// </para>
/// </remarks>
public static class CloudLoginDisplayName
{
    /// <summary>
    /// The display name for a first/last pair, or <see langword="null"/> when neither is set.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string, because every caller already treats "no display name"
    /// as the signal to fall back to an email address or an account id, and an empty string
    /// would pass those checks while rendering as nothing at all.
    /// </remarks>
    public static string? Compose(string? firstName, string? lastName)
    {
        string first = firstName?.Trim() ?? string.Empty;
        string last = lastName?.Trim() ?? string.Empty;

        if (first.Length == 0 && last.Length == 0)
            return null;

        return first.Length == 0 ? last
            : last.Length == 0 ? first
            : $"{first} {last}";
    }
}
