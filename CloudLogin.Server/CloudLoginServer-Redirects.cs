namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    private bool IsAllowedRedirect(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return true;

        if (target.StartsWith('/') && !target.StartsWith("//", StringComparison.Ordinal))
            return true;

        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
            return false;

        if (CloudLoginShared.IsSameOrigin(target, LoginUrl))
            return true;

        if (uri.Scheme is "https" && _configuration.AllowedRedirectOrigins.Count == 0)
            return true;

        if (uri.Scheme is "http" or "https")
            return _configuration.AllowedRedirectOrigins.Any(origin =>
                CloudLoginShared.IsSameOrigin(target, origin));

        return _configuration.AllowedMobileSchemes.Any(scheme =>
            uri.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase));
    }
}
