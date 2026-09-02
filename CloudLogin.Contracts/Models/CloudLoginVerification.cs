namespace AngryMonkey.CloudLogin;

/// <summary>What a verification code is being asked for.</summary>
/// <remarks>
/// The purpose is fixed when the code is issued and checked again when it is redeemed, so a code
/// mailed to prove an address during registration can never be replayed to sign in.
/// </remarks>
public enum CloudLoginVerificationPurposes
{
    /// <summary>Signing in to an existing account.</summary>
    SignIn,

    /// <summary>Proving an address before an account is created for it.</summary>
    Registration,

    /// <summary>Proving an address before its password is replaced.</summary>
    PasswordReset,

    /// <summary>Proving an address before it is added to an existing account.</summary>
    AddInput
}

/// <summary>Asks the server to issue and deliver a verification code.</summary>
public sealed record CloudLoginSendCodeRequest
{
    /// <summary>The email address or phone number the code is delivered to.</summary>
    public required string Address { get; init; }

    /// <summary>What the code is for.</summary>
    public required CloudLoginVerificationPurposes Purpose { get; init; }

    /// <summary>Creates a request.</summary>
    public static CloudLoginSendCodeRequest Create(string address, CloudLoginVerificationPurposes purpose)
        => new() { Address = address, Purpose = purpose };
}

/// <summary>
/// The handle to a code the server issued. It identifies the challenge; it is not the code, and
/// on its own it authenticates nothing.
/// </summary>
public sealed record CloudLoginVerificationChallenge
{
    /// <summary>The challenge this browser redeems its code against.</summary>
    public required string ChallengeId { get; init; }

    /// <summary>When the code stops being accepted.</summary>
    public required DateTimeOffset ExpiresOn { get; init; }
}

/// <summary>Redeems a code against the challenge it was issued for.</summary>
public sealed record CloudLoginVerifyCodeRequest
{
    /// <summary>The challenge handle returned when the code was sent.</summary>
    public required string ChallengeId { get; init; }

    /// <summary>The code the person typed in.</summary>
    public required string Code { get; init; }

    /// <summary>Whether a sign-in completed by this code is persistent.</summary>
    public bool KeepMeSignedIn { get; init; }

    /// <summary>Creates a request.</summary>
    public static CloudLoginVerifyCodeRequest Create(string challengeId, string code, bool keepMeSignedIn = false)
        => new() { ChallengeId = challengeId, Code = code, KeepMeSignedIn = keepMeSignedIn };
}

/// <summary>How redeeming a code turned out.</summary>
public enum CloudLoginVerificationStatuses
{
    /// <summary>The code matched. For <see cref="CloudLoginVerificationPurposes.SignIn"/> the caller is now signed in.</summary>
    Verified,

    /// <summary>The code did not match.</summary>
    Invalid,

    /// <summary>The challenge is unknown, already redeemed, or was never issued.</summary>
    NotFound,

    /// <summary>The code was correct too late.</summary>
    Expired,

    /// <summary>Too many wrong codes; the challenge is dead and a new one must be sent.</summary>
    TooManyAttempts,

    /// <summary>The code matched but no account could be signed in - it has to be created first.</summary>
    NoAccount
}

/// <summary>What redeeming a code produced.</summary>
public sealed record CloudLoginVerificationResult
{
    /// <summary>The outcome.</summary>
    public required CloudLoginVerificationStatuses Status { get; init; }

    /// <summary>Whether the code matched, whatever followed from it.</summary>
    public bool IsVerified => Status is CloudLoginVerificationStatuses.Verified or CloudLoginVerificationStatuses.NoAccount;

    /// <summary>
    /// Proof of the verified address, carried to the call that acts on it - creating the account,
    /// replacing the password, adding the address. Present only for those purposes, single use, and
    /// valid only as long as the challenge itself.
    /// </summary>
    public string? VerificationToken { get; init; }

    /// <summary>How many attempts remain before the challenge is dead.</summary>
    public int AttemptsRemaining { get; init; }

    /// <summary>Creates a result.</summary>
    public static CloudLoginVerificationResult Create(
        CloudLoginVerificationStatuses status,
        string? verificationToken = null,
        int attemptsRemaining = 0)
        => new() { Status = status, VerificationToken = verificationToken, AttemptsRemaining = attemptsRemaining };
}
