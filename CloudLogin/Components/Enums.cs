namespace AngryMonkey.CloudLogin;
public partial class CloudLogin
{
    private enum VerificationCodeResult
    {
        Valid,
        NotValid,
        Expired
    }
    private enum AnimateBodyStep
    {
        None,
        Out,
        In
    }
    private enum AnimateBodyDirection
    {
        None,
        Forward,
        Backward
    }
    protected enum ProcessState
    {
        InputValue,
        Providers,
        Registration,
        CodeVerification,
        ChangePrimary
    }
}
