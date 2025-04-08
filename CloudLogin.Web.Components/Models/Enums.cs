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

public enum ProcessState
{
    InputValue,
    Providers,
    CodeVerification,
    Registration,
    ChangePrimary,
    EmailPasswordLogin,
    EmailPasswordRegister,
    EmailForgetPassword
}