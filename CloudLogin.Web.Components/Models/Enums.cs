namespace AngryMonkey.CloudLogin.Models;

public enum VerificationCodeResult
{
    Valid,
    NotValid,
    Expired
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
    RegistrationInput,   // Step 1: Input email/phone for registration
    RegistrationDetails, // Step 2: Fill in name details for registration
    RegistrationProviders, // Step 3: Choose registration method (Code/Password/Both)
    RegistrationCodeVerification, // Step 4a: Code-only registration verification
    RegistrationPasswordVerification, // Step 4b: Password+Code registration verification
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