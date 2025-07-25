namespace AngryMonkey.CloudLogin;

/// <summary>
/// Request model for password-based login
/// </summary>
public sealed record PasswordLoginRequest
{
    /// <summary>
    /// User's email address or username
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's password
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// Whether to keep the user signed in across browser sessions
    /// </summary>
    public bool KeepMeSignedIn { get; init; } = false;

    /// <summary>
    /// Creates a new PasswordLoginRequest
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="password">User's password</param>
    /// <param name="keepMeSignedIn">Whether to keep user signed in</param>
    /// <returns>A new PasswordLoginRequest instance</returns>
    public static PasswordLoginRequest Create(string email, string password, bool keepMeSignedIn = false)
        => new()
        {
            Email = email,
            Password = password,
            KeepMeSignedIn = keepMeSignedIn
        };
}

/// <summary>
/// Request model for password-based registration with input verification
/// Password registration always requires code verification of the input (email/phone)
/// </summary>
public sealed record PasswordRegistrationRequest
{
    /// <summary>
    /// User's input (email or phone number)
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// The format of the input (email or phone)
    /// </summary>
    public required InputFormat InputFormat { get; init; }

    /// <summary>
    /// User's password
    /// </summary>
    public required string Password { get; init; }

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
    /// Creates a new PasswordRegistrationRequest
    /// </summary>
    /// <param name="input">User's input (email or phone)</param>
    /// <param name="inputFormat">Format of the input</param>
    /// <param name="password">User's password</param>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="displayName">User's display name (optional)</param>
    /// <returns>A new PasswordRegistrationRequest instance</returns>
    public static PasswordRegistrationRequest Create(string input, InputFormat inputFormat, string password, string firstName, string lastName, string? displayName = null)
        => new()
        {
            Input = input,
            InputFormat = inputFormat,
            Password = password,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}"
        };

    /// <summary>
    /// Creates a new PasswordRegistrationRequest for email-only registration (legacy)
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="password">User's password</param>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="displayName">User's display name (optional)</param>
    /// <returns>A new PasswordRegistrationRequest instance</returns>
    public static PasswordRegistrationRequest Create(string email, string password, string firstName, string lastName, string? displayName = null)
        => new()
        {
            Input = email,
            InputFormat = InputFormat.EmailAddress,
            Password = password,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}"
        };
}

/// <summary>
/// Request model for code-only registration (no password)
/// </summary>
public sealed record CodeRegistrationRequest
{
    /// <summary>
    /// User's input (email or phone number)
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// The format of the input (email or phone)
    /// </summary>
    public required InputFormat InputFormat { get; init; }

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
    /// Creates a new CodeRegistrationRequest
    /// </summary>
    /// <param name="input">User's input (email or phone)</param>
    /// <param name="inputFormat">Format of the input</param>
    /// <param name="firstName">User's first name</param>
    /// <param name="lastName">User's last name</param>
    /// <param name="displayName">User's display name (optional)</param>
    /// <returns>A new CodeRegistrationRequest instance</returns>
    public static CodeRegistrationRequest Create(string input, InputFormat inputFormat, string firstName, string lastName, string? displayName = null)
        => new()
        {
            Input = input,
            InputFormat = inputFormat,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}"
        };
}