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

        /// <summary>Blob service endpoint reached with a credential rather than an account key.</summary>
        public const string BlobEndpoint = "Storage:BlobEndpoint";

        /// <summary>Account name, reached with a credential rather than a key.</summary>
        public const string AccountName = "Storage:AccountName";

        /// <summary>Blob container holding CloudLogin's own files.</summary>
        public const string ContainerName = "Storage:ContainerName";
    }

    /// <summary>Token-authority configuration inferred from CloudLogin resource references.</summary>
    public static class Tokens
    {
        /// <summary>The absolute issuer URI used to mint and validate CloudLogin tokens.</summary>
        public const string Issuer = "CloudLoginTokens:Issuer";

        /// <summary>The audiences for which the authority may mint tokens.</summary>
        public const string AllowedAudiences = "CloudLoginTokens:AllowedAudiences";

        /// <summary>The confidential service clients allowed to request downstream tokens.</summary>
        public const string ServiceClients = "CloudLoginTokens:ServiceClients";
    }

    /// <summary>Relying-party token configuration inferred from its CloudLogin reference.</summary>
    public static class Client
    {
        /// <summary>The absolute URI of the CloudLogin authority.</summary>
        public const string Authority = "CloudLogin:Authority";

        /// <summary>The audience identifying the relying application.</summary>
        public const string Audience = "CloudLogin:Audience";

        /// <summary>The relying application's confidential client identifier.</summary>
        public const string ClientId = "CloudLogin:ClientId";

        /// <summary>The generated secret shared with the CloudLogin authority.</summary>
        public const string ClientSecret = "CloudLogin:ClientSecret";

        /// <summary>
        /// The other CloudLogin-protected services this application calls, as
        /// audience/base-address pairs.
        /// <para>
        /// An access token is only accepted by the one service it names, so a call to a
        /// sibling service needs a token minted for that sibling. Publishing the mapping
        /// here lets the outbound token handler work that out from the request's address,
        /// which means no call site has to name an audience and no deployment has to
        /// restate a relationship the AppHost already declares.
        /// </para>
        /// </summary>
        public const string DownstreamServices = "CloudLogin:DownstreamServices";
    }

    /// <summary>The primary color used by CloudLogin's UI.</summary>
    public const string PrimaryColor = "CloudLogin:PrimaryColor";

    /// <summary>The CloudLogin page title.</summary>
    public const string Title = "CloudLogin:Title";

    /// <summary>The Microsoft identity-provider configuration section.</summary>
    public const string MicrosoftProvider = "Microsoft";

    /// <summary>The Google identity-provider configuration section.</summary>
    public const string GoogleProvider = "Google";

    /// <summary>The origin a relying party reaches the CloudLogin authority on.</summary>
    public const string LoginUrl = "LoginUrl";

    /// <summary>Origins CloudLogin will redirect a signed-in user back to.</summary>
    public const string AllowedRedirectOrigins = "CloudLogin:AllowedRedirectOrigins";
}
