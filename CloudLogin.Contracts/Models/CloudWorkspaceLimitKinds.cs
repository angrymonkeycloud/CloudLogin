namespace AngryMonkey.CloudLogin;

/// <summary>Which of a user's workspace caps was reached.</summary>
public enum CloudWorkspaceLimitKinds
{
    /// <summary>The cap on workspaces the user may create and own.</summary>
    Owned,

    /// <summary>The cap on workspaces the user may belong to in total.</summary>
    Membership
}
