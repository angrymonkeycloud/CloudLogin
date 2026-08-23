namespace AngryMonkey.CloudLogin;

/// <summary>
/// Defaults for how many workspaces a single user may own or belong to. A host overrides
/// them through <c>WorkspaceConfiguration</c>; the values here apply when it leaves them unset.
/// </summary>
public static class CloudWorkspaceLimits
{
    /// <summary>Workspaces one user may create when the host doesn't configure a cap.</summary>
    public const int DefaultMaxOwnedPerUser = 10;

    /// <summary>Workspaces one user may belong to in total when the host doesn't configure a cap.</summary>
    public const int DefaultMaxPerUser = 20;

    /// <summary>Assign to a cap to remove it entirely.</summary>
    public const int Unlimited = int.MaxValue;
}
