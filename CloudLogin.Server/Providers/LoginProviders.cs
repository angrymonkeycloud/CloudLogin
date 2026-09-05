using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
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
            HandleUpdateOnly = false;
            HandlesEmailAddress = true;
            InputRequired = true;
            IsCodeVerification = true;
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
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public string? TenantId { get; set; }
        public Uri? VaultEndpoint { get; set; }
        public string? CertificateName { get; set; }
        public MicrosoftProviderAudience Audience { get; set; } = MicrosoftProviderAudience.All;



        public MicrosoftProviderConfiguration(string label = "Microsoft")
        {
            Init("Microsoft", true, label);
            HandlesEmailAddress = true;
        }

        internal async Task<X509Certificate2> GetCertificate(CancellationToken cancellationToken = default)
        {

            if (VaultEndpoint is null)
                throw new InvalidOperationException(
                    "Microsoft sign-in requires Microsoft:VaultEndpoint when no client secret is configured.");

            if (string.IsNullOrWhiteSpace(CertificateName))
                throw new InvalidOperationException(
                    "Microsoft sign-in requires Microsoft:CertificateName when no client secret is configured.");

            CertificateClient client = new(VaultEndpoint, new DefaultAzureCredential());
            Azure.Response<X509Certificate2> response = await client.DownloadCertificateAsync(
                CertificateName,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response.Value;
        }


        public MicrosoftProviderConfiguration(IConfigurationSection configurationSection, bool handleUpdateOnly = false)
            : this(configurationSection["Label"] ?? "Microsoft")
        {
            ClientId = configurationSection["ClientId"] ?? string.Empty;
            ClientSecret = configurationSection["ClientSecret"];
            CertificateName = configurationSection["CertificateName"];
            TenantId = configurationSection["TenantId"];
            
            if (Uri.TryCreate(configurationSection["VaultEndpoint"], UriKind.Absolute, out Uri? vaultEndpoint))
                VaultEndpoint = vaultEndpoint;
            
            if (Enum.TryParse(configurationSection["Audience"], ignoreCase: true, out MicrosoftProviderAudience audience))
                Audience = audience;

            HandleUpdateOnly = configurationSection.GetValue("HandleUpdateOnly", handleUpdateOnly);
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

            Init("Facebook", true, label);
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