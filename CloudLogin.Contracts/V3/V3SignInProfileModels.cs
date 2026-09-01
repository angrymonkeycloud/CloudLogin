namespace AngryMonkey.CloudLogin.V3;

/// <summary>The login page's view of its resolved sign-in profile.</summary>
public sealed record V3SignInProfileResponse
{
    public required string Name { get; init; }

    /// <summary>Entry methods the page displays. Already filtered against the deployment's providers.</summary>
    public List<string> VisibleMethods { get; init; } = [];

    /// <summary>
    /// Opaque protected state binding this resolution; must be sent back on completion calls so
    /// URL tampering cannot switch profiles mid-flow.
    /// </summary>
    public required string BoundState { get; init; }
}
