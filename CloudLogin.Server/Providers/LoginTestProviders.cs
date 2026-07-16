using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Sever.Providers;

/// <summary>
/// Explicitly configured test-user authentication providers.
/// </summary>
public class LoginTestProviders
{
    /// <summary>
    /// Test-mode provider configuration.
    /// When <see cref="IsEnabled"/> is true:
    /// - Registration skips password and verification-code steps.
    /// - Created users only have basic inputs (Format, Input, IsPrimary) with no providers.
    /// - Created users are flagged as test users (<see cref="UserModel.IsTest"/> = true).
    /// </summary>
    public class TestModeConfiguration : ProviderConfiguration
    {
        /// <summary>
        /// When true, the provider operates in test mode in the current host
        /// environment. The default is false.
        /// </summary>
        public bool IsEnabled { get; init; }

        public TestModeConfiguration(IConfigurationSection configurationSection)
        {
            IsEnabled = configurationSection.GetValue("IsEnabled", false);

            string? label = configurationSection["Label"] ?? "Test Mode";
            Init("testmode", false, label);
            HandleUpdateOnly = false;
            HandlesEmailAddress = true;
            InputRequired = false;
            IsCodeVerification = false;
        }

        public override ProviderDefinition ToModel() => base.ToModel();
    }
}
