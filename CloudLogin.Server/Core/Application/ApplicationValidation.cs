namespace AngryMonkey.CloudLogin.Server.Core.Application;

public static class ApplicationValidation
{
    private static readonly HashSet<string> ForbiddenSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "file", "vbscript", "data", "blob"
    };

    /// <summary>
    /// A return or post-logout URL: https (http only on loopback), or a custom scheme when <paramref name="allowCustomScheme"/>, with no
    /// credentials or fragment. <paramref name="normalized"/> is the URL in canonical form.
    /// </summary>
    public static bool TryNormalizeUrl(string? value, bool allowCustomScheme, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
            return false;

        string candidate = value.Trim();

        if (candidate.Contains('\\') || candidate.Any(char.IsControl))
            return false;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
            return false;

        if (ForbiddenSchemes.Contains(uri.Scheme) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            if (string.IsNullOrEmpty(uri.Host))
                return false;
        }
        else if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!uri.IsLoopback)
                return false;
        }
        else if (!allowCustomScheme)
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }
}
