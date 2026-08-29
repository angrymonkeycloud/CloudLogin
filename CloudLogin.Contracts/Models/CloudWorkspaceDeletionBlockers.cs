namespace AngryMonkey.CloudLogin;

/// <summary>
/// Reasons a workspace can't be deleted yet. CloudLogin itself holds nothing that blocks
/// deletion any more; the flags shape is kept so an application-supplied report can add its own
/// reasons later without a wire change.
/// </summary>
[Flags]
public enum CloudWorkspaceDeletionBlockers
{
    None = 0
}
