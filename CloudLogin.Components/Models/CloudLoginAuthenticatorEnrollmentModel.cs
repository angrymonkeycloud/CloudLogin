namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginAuthenticatorEnrollmentModel
{
    public string SecretKey { get; set; } = string.Empty;
    public string ProvisioningUri { get; set; } = string.Empty;
}

public static class CloudLoginAuthenticatorEnrollmentModelExtensions
{
    public static CloudLoginAuthenticatorEnrollmentModel ToModel(this CloudLoginAuthenticatorEnrollment source) => new()
    {
        SecretKey = source.SecretKey,
        ProvisioningUri = source.ProvisioningUri
    };
}
