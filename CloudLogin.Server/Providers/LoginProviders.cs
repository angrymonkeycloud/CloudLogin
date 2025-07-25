
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace AngryMonkey.CloudLogin.Sever.Providers;

public class LoginProviders
{
    public class PasswordProviderConfiguration : ProviderConfiguration
    {
        public PasswordProviderConfiguration(IConfigurationSection configurationSection)
        {
            string? label = configurationSection["Label"] ?? "Email/Password";
            Init("password", false, label);
            HandleUpdateOnly = true;
            HandlesEmailAddress = true;
            InputRequired = true;
            IsCodeVerification = false;
        }
    }

    public class CodeProviderConfiguration : ProviderConfiguration
    {
        public CodeProviderConfiguration(IConfigurationSection configurationSection)
        {
            string? label = configurationSection["Label"] ?? "Email/Code";
            Init("code", false, label);
            HandleUpdateOnly = true;
            HandlesEmailAddress = true;
            InputRequired = true;
            IsCodeVerification = false;
        }
    }

    //public class CustomProviderConfiguration : ProviderConfiguration
    //{
    //    public CustomProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
    //    {
    //        string label = configurationSection["Label"] ?? "Custom";
    //        Init("custom", label);

    //        HandleUpdateOnly = handleUpdateOnly;
    //        HandlesEmailAddress = true;
    //        InputRequired = true;
    //        IsCodeVerification = true;
    //    }
    //}

    public class MicrosoftProviderConfiguration : ProviderConfiguration
    {
        public string ClientId { get; init; } = string.Empty;
        public string? ClientSecret { get; init; }
        public string? TenantId { get; init; }
        public Uri? VaultEndpoint { get; init; }
        public MicrosoftProviderAudience Audience { get; set; } = MicrosoftProviderAudience.All;

        //public MicrosoftProviderConfiguration(string? label = null) : base("Microsoft", label)
        //{
        //	HandlesEmailAddress = true;
        //}

        private X509Certificate2? _certification;
        internal async Task<X509Certificate2> GetCertificate()
        {
            if (_certification != null)
                return _certification;

            if (VaultEndpoint == null)
                throw new ArgumentNullException(nameof(VaultEndpoint));

            CertificateClient client = new(VaultEndpoint, new DefaultAzureCredential());

            // Coconust Sharp
            //Azure.Response<X509Certificate2> response = await client.DownloadCertificateAsync(CoconutSharpDefaults.App_Certificate);

            //_certification = response.Value;

            //return _certification;

            return null;
        }

        private MicrosoftProviderConfiguration() { }

        public MicrosoftProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
        {
            ClientId = configurationSection["ClientId"];
            ClientSecret = configurationSection["ClientSecret"];
            string label = configurationSection["Label"];

            Init("Microsoft", true, label);
            HandleUpdateOnly = handleUpdateOnly;
            HandlesEmailAddress = true;
        }

        public static async Task<MicrosoftProviderConfiguration> FromAzureVault(Uri vaultEndpoint, string tenantId, bool handleUpdateOnly = false, string label = "Microsoft")
        {
            SecretClient client = new(vaultEndpoint, new DefaultAzureCredential());

            MicrosoftProviderConfiguration configuration = new()
            {
                // Coconust Sharp
                ClientId = null, // (await client.GetSecretAsync(CoconutSharpDefaults.App_ClientId)).Value.Value,
                VaultEndpoint = vaultEndpoint,
                TenantId = tenantId,
                Label = label,
                HandleUpdateOnly = handleUpdateOnly,
                HandlesEmailAddress = true
            };

            configuration.Init("Microsoft", true, label);

            return configuration;
        }
    }

    public class GoogleProviderConfiguration : ProviderConfiguration
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public GoogleProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
        {
            ClientId = configurationSection["ClientId"];
            ClientSecret = configurationSection["ClientSecret"];
            string label = configurationSection["Label"];

            Init("Google", true, label);
            HandleUpdateOnly = handleUpdateOnly;
            HandlesEmailAddress = true;
        }
    }

    public class FacebookProviderConfiguration : ProviderConfiguration
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public FacebookProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
        {
            ClientId = configurationSection["ClientId"];
            ClientSecret = configurationSection["ClientSecret"];
            string label = configurationSection["Label"];

            Init("Facbook", true, label);
            HandleUpdateOnly = handleUpdateOnly;
            HandlesEmailAddress = true;
        }
    }

    public class TwitterProviderConfiguration : ProviderConfiguration
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;

        public TwitterProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
        {
            ClientId = configurationSection["ClientId"];
            ClientSecret = configurationSection["ClientSecret"];
            string label = configurationSection["Label"];

            Init("Twitter", true, label);
            HandleUpdateOnly = handleUpdateOnly;
            HandlesEmailAddress = true;
        }
    }

    public class WhatsAppProviderConfiguration : ProviderConfiguration
    {
        public string RequestUri { get; set; }
        public string Authorization { get; set; }
        public string Template { get; set; }
        public string Language { get; set; }

        public WhatsAppProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
        {
            RequestUri = configurationSection["RequestUri"];
            Authorization = configurationSection["Authorization"];
            Template = configurationSection["Template"];
            Language = configurationSection["Language"];
            string label = configurationSection["Label"];

            Init("WhatsApp", true, label);
            HandleUpdateOnly = handleUpdateOnly;
            HandlesPhoneNumber = true;
            InputRequired = true;
            IsCodeVerification = true;
        }
    }
}