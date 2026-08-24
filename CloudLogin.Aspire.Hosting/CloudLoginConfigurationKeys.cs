namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// The configuration keys the CloudLogin server reads, named once so a host never has to spell them
/// out and a rename here reaches every host that binds them.
/// </summary>
/// <remarks>
/// Written with <c>:</c>, .NET's own configuration separator - <c>Cosmos:ConnectionString</c> is the
/// <c>Cosmos</c> section's <c>ConnectionString</c>, the same setting whether it arrives from
/// appsettings.json, an environment variable, or a deployment tool writing application settings.
/// Hosts that need the <c>__</c> encoding (a Linux container or App Service site, where <c>:</c> is
/// not portable in an environment variable name) are expected to translate on the way out; the
/// canonical spelling does not change to suit one transport.
/// </remarks>
public static class CloudLoginConfigurationKeys
{
    /// <summary>The Cosmos section holding CloudLogin's user store.</summary>
    public static class Cosmos
    {
        /// <summary>Account key connection string. Mutually exclusive with <see cref="AccountEndpoint"/>.</summary>
        public const string ConnectionString = "Cosmos:ConnectionString";

        /// <summary>Account endpoint, reached with a credential rather than a key.</summary>
        public const string AccountEndpoint = "Cosmos:AccountEndpoint";

        /// <summary>Database holding the user container.</summary>
        public const string DatabaseId = "Cosmos:DatabaseId";

        /// <summary>Container holding user records.</summary>
        public const string ContainerId = "Cosmos:ContainerId";

        /// <summary>Forces Gateway connection mode, which local Cosmos emulators require.</summary>
        public const string GatewayMode = "Cosmos:GatewayMode";
    }

    /// <summary>The Azure Storage section holding profile pictures and security documents.</summary>
    public static class Storage
    {
        /// <summary>Account key connection string. Mutually exclusive with <see cref="AccountName"/>.</summary>
        public const string ConnectionString = "Storage:ConnectionString";

        /// <summary>Account name, reached with a credential rather than a key.</summary>
        public const string AccountName = "Storage:AccountName";

        /// <summary>Blob container holding CloudLogin's own files.</summary>
        public const string ContainerName = "Storage:ContainerName";
    }

    /// <summary>The origin a relying party reaches the CloudLogin authority on.</summary>
    public const string LoginUrl = "LoginUrl";

    /// <summary>Origins CloudLogin will redirect a signed-in user back to.</summary>
    public const string AllowedRedirectOrigins = "CloudLogin:AllowedRedirectOrigins";
}
