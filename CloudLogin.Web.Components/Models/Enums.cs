namespace AngryMonkey.CloudLogin.Models;

public enum VerificationCodeResult
{
    Valid,
    NotValid,
    Expired
}

public enum AnimateBodyStep
{
    None,
    Out,
    In
}

public enum AnimateBodyDirection
{
    None,
    Forward,
    Backward
}

public enum ProcessStep
{
    None,
    InputValue,          // Get input value from user (email/phone)
    Providers,           // Select authentication provider
    CodeVerification,    // Verify code (OTP)
    CodeEmailVerification,    // Verify code (OTP)
    Registration,        // Create a new account
    EmailPasswordLogin,  // Password-based login
    EmailPasswordRegister, // Register with password
    EmailForgetPassword, // Reset forgotten password
    ChangePrimary        // Change primary email
}

public enum AuthenticationProcess
{
    None,
    StandardLogin,       // Standard login flow
    PasswordLogin,       // Password-based login
    OtpLogin,            // One-time password login
    Registration,        // New account registration
    PasswordReset,       // Reset forgotten password
    UpdateAccount,       // Update account information
    AddInput,            // Add new login method
    ChangePrimary        // Change primary email
}