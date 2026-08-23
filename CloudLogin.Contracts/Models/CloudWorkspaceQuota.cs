namespace AngryMonkey.CloudLogin;

/// <summary>
/// How much of a user's workspace allowance is used. <see cref="Total"/> counts every
/// workspace the user belongs to, owned ones included, so it is never below <see cref="Owned"/>.
/// </summary>
public sealed record CloudWorkspaceQuota
{
    public required int Owned { get; init; }
    public required int MaxOwned { get; init; }
    public required int Total { get; init; }
    public required int MaxTotal { get; init; }

    public bool OwnedIsUnlimited => MaxOwned >= CloudWorkspaceLimits.Unlimited;
    public bool TotalIsUnlimited => MaxTotal >= CloudWorkspaceLimits.Unlimited;

    public int RemainingOwned => OwnedIsUnlimited ? CloudWorkspaceLimits.Unlimited : Math.Max(0, MaxOwned - Owned);
    public int RemainingTotal => TotalIsUnlimited ? CloudWorkspaceLimits.Unlimited : Math.Max(0, MaxTotal - Total);

    /// <summary>A new workspace counts against both caps, so both must have room.</summary>
    public bool CanCreate => RemainingOwned > 0 && RemainingTotal > 0;

    /// <summary>Joining someone else's workspace counts only against the membership cap.</summary>
    public bool CanJoin => RemainingTotal > 0;
}
