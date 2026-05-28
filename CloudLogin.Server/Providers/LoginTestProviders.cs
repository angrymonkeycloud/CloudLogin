using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Sever.Providers;

/// <summary>
/// Test/dev providers that mirror the real ones but use shared, well-known credentials.
/// Intended for local / staging environments only.
/// </summary>
public class LoginTestProviders
{
    /// <summary>
    /// Test-mode variant of <see cref="LoginProviders.PasswordProviderConfiguration"/>.
    /// When <see cref="IsEnabled"/> is true and <see cref="Password"/> is non-empty:
    /// - Registration via this provider skips the password and verification-code steps.
    /// - The configured <see cref="Password"/> is hashed and stored as the user's password.
    /// - Created users are flagged as test users (<see cref="UserModel.IsTest"/> = true).
    /// - Test users can sign in by typing the configured <see cref="Password"/>.
    /// Registers under the same provider code ("password") as the real password provider, so
    /// only one of them should be added to <see cref="CloudLoginWebConfiguration.Providers"/>.
    /// </summary>
    public class PasswordProviderTestConfiguration : ProviderConfiguration
    {
        /// <summary>
        /// When true, the provider operates in test mode.
        /// </summary>
        public bool IsEnabled { get; init; }

        /// <summary>
        /// Shared password used when <see cref="IsEnabled"/> is true.
        /// </summary>
        public string? Password { get; init; }

        public PasswordProviderTestConfiguration(IConfigurationSection configurationSection)
        {
            IsEnabled = configurationSection.GetValue("IsEnabled", false);
            Password = configurationSection["Password"];

            string? label = configurationSection["Label"] ?? "Test Email";
            Init("password", false, label);
            HandleUpdateOnly = true;
            HandlesEmailAddress = true;
            InputRequired = true;
            IsCodeVerification = false;
        }

        /// <summary>
        /// True when the provider is enabled and a shared password is configured.
        /// </summary>
        public bool IsTestEnabled => IsEnabled && !string.IsNullOrWhiteSpace(Password);

        public override ProviderDefinition ToModel()
        {
            ProviderDefinition model = base.ToModel();
            model.IsTest = IsTestEnabled;
            return model;
        }
    }
}
