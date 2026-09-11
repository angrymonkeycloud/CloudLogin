namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// Shapes the CloudLogin project <c>AddCloudLoginProject</c> generates: the ports it listens on
/// during a local run and the static files it serves.
/// </summary>
public sealed class CloudLoginProjectOptions
{
    /// <summary>
    /// The local HTTPS port. Fixed rather than allocated per run, because external sign-in
    /// providers register their redirect URIs against it.
    /// </summary>
    public int HttpsPort { get; set; } = 7100;

    /// <summary>The local HTTP port.</summary>
    public int HttpPort { get; set; } = 5100;

    /// <summary>
    /// A directory whose files the site serves as its web root - a <c>logo.svg</c> for the account
    /// page, for example. A relative path resolves against the AppHost directory.
    /// </summary>
    public string? WebRootPath { get; set; }
}
