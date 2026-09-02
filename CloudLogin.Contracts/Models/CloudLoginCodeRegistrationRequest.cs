namespace AngryMonkey.CloudLogin;

/// <summary>
/// Request model for code-only registration (no password)
/// </summary>
public sealed record CloudLoginCodeRegistrationRequest
{
    /// <summary>
    /// User's input (email or phone number)
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// The format of the input (email or phone)
    /// </summary>
    public required CloudLoginInputFormat InputFormat { get; init; }

    /// <summary>
    /// User's first name
    /// </summary>
    public required string FirstName { get; init; }

    /// <summary>
    /// User's last name
    /// </summary>
    public required string LastName { get; init; }

    /// <summary>
    /// User's display name
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Proof that the code sent to <see cref="Input"/> was answered correctly, from
    /// <see cref="CloudLoginVerificationResult.VerificationToken"/>. Registration without it is
    /// refused - otherwise an account could be created for an address nobody had proven.
    /// </summary>
    public string? VerificationToken { get; init; }

    /// <summary>Whether the sign-in that completes the registration is persistent.</summary>
    public bool KeepMeSignedIn { get; init; }

    /// <summary>
    /// Creates a new CloudLoginCodeRegistrationRequest
    /// </summary>
    /// <param name="input">User's input (email or phone)</param>
    /// <param name="inputFormat">Format of the input</param>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="displayName">User's display name (optional)</param>
    /// <param name="verificationToken">Proof the address was verified</param>
    /// <param name="keepMeSignedIn">Whether the resulting sign-in is persistent</param>
    /// <returns>A new CloudLoginCodeRegistrationRequest instance</returns>
    public static CloudLoginCodeRegistrationRequest Create(
        string input,
        CloudLoginInputFormat inputFormat,
        string firstName,
        string lastName,
        string? displayName = null,
        string? verificationToken = null,
        bool keepMeSignedIn = false)
        => new()
        {
            Input = input,
            InputFormat = inputFormat,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}",
            VerificationToken = verificationToken,
            KeepMeSignedIn = keepMeSignedIn
        };
}
