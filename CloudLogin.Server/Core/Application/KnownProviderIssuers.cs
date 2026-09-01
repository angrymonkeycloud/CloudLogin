namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// The provider codes whose issuer is known safely. Everything identity-critical keys off
/// <c>(realm, issuer, subject)</c>; a provider absent from this map gets a private
/// <c>provider:{code}</c> issuer namespace instead of a guessed URL, and the migration flags its
/// identifiers for review rather than converting them.
/// </summary>
public static class KnownProviderIssuers
{
    private static readonly Dictionary<string, string> Issuers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google"] = "https://accounts.google.com",
        ["Microsoft"] = "https://login.microsoftonline.com/common/v2.0",
        ["Facebook"] = "https://www.facebook.com",
        ["Twitter"] = "https://twitter.com"
    };

    public static bool TryGet(string providerCode, out string issuer) =>
        Issuers.TryGetValue(providerCode, out issuer!);

    /// <summary>The issuer string used for a provider, falling back to its private namespace.</summary>
    public static string GetOrFallback(string providerCode) =>
        TryGet(providerCode, out string issuer) ? issuer : $"provider:{providerCode.ToLowerInvariant()}";
}
