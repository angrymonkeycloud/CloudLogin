namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    /// <summary>
    /// Schemes that can never be a legitimate redirect target regardless of
    /// configuration — they don't identify a website or a mobile app, so no
    /// allowlist decision applies to them; they execute in the browser
    /// (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>) or resolve to the
    /// local machine (<c>file:</c>, <c>blob:</c>) instead of navigating away.
    /// </summary>
    private static readonly HashSet<string> ForbiddenRedirectSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", Uri.UriSchemeFile, "vbscript", "data", "blob"
    };

    private static bool IsRelativePath(string target) =>
        target.StartsWith('/') && !target.StartsWith("//", StringComparison.Ordinal);

    private bool IsAllowedRedirect(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return true;

        if (IsRelativePath(target))
            return true;

        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
            return false;

        if (ForbiddenRedirectSchemes.Contains(uri.Scheme))
            return false;

        if (CloudLoginShared.IsSameOrigin(target, LoginUrl))
            return true;

        // No websites/apps have been registered for this channel, so there is
        // nothing to restrict against — allow every destination by default.
        // Once the application lists at least one allowed origin/scheme, only
        // that list is trusted.
        if (uri.Scheme is "http" or "https")
            return _configuration.AllowedRedirectOrigins.Count == 0 ||
                _configuration.AllowedRedirectOrigins.Any(origin =>
                    CloudLoginShared.IsSameOrigin(target, origin));

        return _configuration.AllowedMobileSchemes.Count == 0 ||
            _configuration.AllowedMobileSchemes.Any(scheme =>
                uri.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase));
    }
}
