namespace AngryMonkey.CloudLogin;

/// <summary>Thrown when creating or joining a workspace would exceed a configured cap.</summary>
public sealed class CloudWorkspaceLimitReachedException(CloudWorkspaceLimitKinds kind, int limit, string singularLabel = "workspace", string pluralLabel = "workspaces")
    : InvalidOperationException(kind == CloudWorkspaceLimitKinds.Owned
        ? $"This account has reached its limit of {limit} {(limit == 1 ? singularLabel : pluralLabel)} it can create."
        : $"This account has reached its limit of {limit} {(limit == 1 ? singularLabel : pluralLabel)} it can belong to.")
{
    public CloudWorkspaceLimitKinds Kind { get; } = kind;
    public int Limit { get; } = limit;
}
