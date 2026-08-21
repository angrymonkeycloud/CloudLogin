namespace AngryMonkey.CloudLogin;

/// <summary>Small display helpers shared across the account page's cards and switchers.</summary>
internal static class AccountDisplayHelpers
{
    /// <summary>Up to two uppercase initials from a name, falling back to "#" for a name with no letters or digits.</summary>
    public static string Initials(string value)
    {
        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string initials = string.Concat(words.Where(word => char.IsLetterOrDigit(word[0])).Take(2).Select(word => char.ToUpperInvariant(word[0])));

        return string.IsNullOrEmpty(initials) ? "#" : initials;
    }
}
