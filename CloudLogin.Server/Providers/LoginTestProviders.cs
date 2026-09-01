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
    /// - Created users are flagged as test users (<see cref="CloudUser.IsTest"/> = true).
    /// </summary>
    public class TestModeConfiguration : ProviderConfiguration
    {
        /// <summary>
        /// When true, the provider operates in test mode in the current host
        /// environment. The default is false.
        /// </summary>
        public bool IsEnabled { get; init; }

        public TestModeConfiguration(IConfigurationSection configurationSection)
            : this(configurationSection.GetValue("IsEnabled", false), configurationSection["Label"])
        {
        }

        /// <summary>
        /// Configures test mode directly, for a host that decides it in code rather than from a
        /// configuration section — an AppHost enabling it for one environment only, where a shared
        /// settings file would enable it everywhere.
        /// </summary>
        public TestModeConfiguration(bool isEnabled = false, string? label = null)
        {
            IsEnabled = isEnabled;

            Init("testmode", false, label ?? "Test Mode");
            HandleUpdateOnly = false;
            HandlesEmailAddress = true;
            InputRequired = false;
            IsCodeVerification = false;
        }

        public override CloudLoginProviderDefinition ToModel() => base.ToModel();
    }
}
