namespace AngryMonkey.CloudLogin;

/// <summary>
/// Request model for password-based login
/// </summary>
public sealed record CloudLoginPasswordLoginRequest
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
    /// Creates a new CloudLoginPasswordLoginRequest
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="password">User's password</param>
    /// <param name="keepMeSignedIn">Whether to keep user signed in</param>
    /// <returns>A new CloudLoginPasswordLoginRequest instance</returns>
    public static CloudLoginPasswordLoginRequest Create(string email, string password, bool keepMeSignedIn = false)
        => new()
        {
            Email = email,
            Password = password,
            KeepMeSignedIn = keepMeSignedIn
        };
}
