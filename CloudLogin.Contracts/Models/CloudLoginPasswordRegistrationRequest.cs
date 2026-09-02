namespace AngryMonkey.CloudLogin;

/// <summary>
/// Request model for password-based registration with input verification
/// Password registration always requires code verification of the input (email/phone)
/// </summary>
public sealed record CloudLoginPasswordRegistrationRequest
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
    /// User's password. Optional when the server-side Password provider is configured in test mode.
    /// </summary>
    public string? Password { get; init; }

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

    /// <summary>Single-use proof that the email address or phone number was verified.</summary>
    public string? VerificationToken { get; init; }

    /// <summary>Whether the server-created session should persist beyond this browser session.</summary>
    public bool KeepMeSignedIn { get; init; }

    /// <summary>
    /// Creates a new CloudLoginPasswordRegistrationRequest
    /// </summary>
    /// <param name="input">User's input (email or phone)</param>
    /// <param name="inputFormat">Format of the input</param>
    /// <param name="password">User's password</param>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="displayName">User's display name (optional)</param>
    /// <returns>A new CloudLoginPasswordRegistrationRequest instance</returns>
    public static CloudLoginPasswordRegistrationRequest Create(
        string input,
        CloudLoginInputFormat inputFormat,
        string? password,
        string firstName,
        string lastName,
        string? displayName = null,
        string? verificationToken = null,
        bool keepMeSignedIn = false)
        => new()
        {
            Input = input,
            InputFormat = inputFormat,
            Password = password,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}",
            VerificationToken = verificationToken,
            KeepMeSignedIn = keepMeSignedIn
        };

    /// <summary>
    /// Creates a new CloudLoginPasswordRegistrationRequest for email-only registration (legacy)
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="password">User's password</param>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="displayName">User's display name (optional)</param>
    /// <returns>A new CloudLoginPasswordRegistrationRequest instance</returns>
    public static CloudLoginPasswordRegistrationRequest Create(
        string email,
        string? password,
        string firstName,
        string lastName,
        string? displayName = null,
        string? verificationToken = null,
        bool keepMeSignedIn = false)
        => new()
        {
            Input = email,
            InputFormat = CloudLoginInputFormat.EmailAddress,
            Password = password,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}",
            VerificationToken = verificationToken,
            KeepMeSignedIn = keepMeSignedIn
        };
}
