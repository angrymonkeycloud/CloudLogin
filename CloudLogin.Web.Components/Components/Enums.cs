namespace AngryMonkey.CloudLogin;

internal enum VerificationCodeResult
{
    Valid,
    NotValid,
    Expired
}

internal enum AnimateBodyStep
{
    None,
    Out,
    In
}

internal enum AnimateBodyDirection
{
    None,
    Forward,
    Backward
}

internal enum ProcessState
{
    InputValue,
    Providers,
    CodeVerification,
    Registration,
    ChangePrimary,
    EmailPasswordLogin,
    EmailPasswordRegister
}