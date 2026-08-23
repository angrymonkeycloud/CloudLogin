namespace AngryMonkey.CloudLogin;

/// <summary>Request to set or change the signed-in user's password.</summary>
public sealed record CloudLoginChangePasswordRequest
{
    /// <summary>Existing password. Required when the account already has one.</summary>
    public string? CurrentPassword { get; init; }

    public required string NewPassword { get; init; }
}
