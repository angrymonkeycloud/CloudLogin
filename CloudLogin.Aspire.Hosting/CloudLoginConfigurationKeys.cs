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

        /// <summary>
        /// Table service endpoint for the core identity index (IdentityKeys, UserWorkspaceIndex),
        /// reached with a credential rather than an account key.
        /// </summary>
        public const string TableEndpoint = "Storage:TableEndpoint";

        /// <summary>Account name, reached with a credential rather than a key.</summary>
        public const string AccountName = "Storage:AccountName";

        /// <summary>Blob container holding CloudLogin's own files.</summary>
        public const string ContainerName = "Storage:ContainerName";
    }

    /// <summary>The CloudLogin storage core (see docs/architecture-core.md).</summary>
    public static class Core
    {
        /// <summary>The whole storage configuration section.</summary>
        public const string Section = "CloudLogin:Core";

        /// <summary>The deployment realm isolating identity keys and audit partitions.</summary>
        public const string RealmId = "CloudLogin:Core:RealmId";

        /// <summary>The Cosmos database holding the seven core containers.</summary>
        public const string DatabaseId = "CloudLogin:Core:DatabaseId";
    }

    /// <summary>
    /// The secret keying the identity index, in its logical configuration form.
    /// </summary>
    public const string IdentityHmacSecret = "CloudLogin:IdentityHmacSecret";

    /// <summary>
    /// The environment-variable spelling of <see cref="IdentityHmacSecret"/>, which is what is
    /// actually injected. Double underscores rather than a colon because that is the form every
    /// host accepts - Linux App Service and containers will not take a colon in a variable name -
    /// so the same wiring works whether the server runs locally or deployed.
    /// <para>
    /// Always supplied through an Aspire parameter, never as a literal: see
    /// <c>WithIdentityHmacSecret</c> and <c>IdentityHmacSecretParameter</c>.
    /// </para>
    /// </summary>
    public const string IdentityHmacSecretVariable = "CloudLogin__IdentityHmacSecret";

    /// <summary>One JSON-array setting containing old read-only identity HMAC keys.</summary>
    public const string IdentityHmacFallbackSecrets = "CloudLogin:IdentityHmacFallbackSecrets";

    /// <summary>The portable environment-variable spelling of <see cref="IdentityHmacFallbackSecrets"/>.</summary>
    public const string IdentityHmacFallbackSecretsVariable = "CloudLogin__IdentityHmacFallbackSecrets";

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

    /// <summary>
    /// The server-to-server channel a trusted backend reads CloudLogin-owned records over - the
    /// Business, Contact and Subscription rows CloudLogin owns and other components only display.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Client"/> on purpose. Those keys describe a user signing in; this
    /// is a backend credential presented on <c>CloudLogin/Service/*</c>, which no browser session
    /// can reach and which bypasses user identity entirely - so it is granted per caller rather
    /// than to everything that references the authority. The authority accepts a list, which is
    /// what lets one caller's key be revoked without touching another's.
    /// </remarks>
    public static class Service
    {
        /// <summary>The CloudLogin site the service endpoints live on, read by the caller.</summary>
        public const string BaseUrl = "CloudLogin:BaseUrl";

        /// <summary>The secret the caller sends as <c>X-CloudLogin-ServiceKey</c>.</summary>
        public const string CallerKey = "CloudLogin:ServiceKey";

        /// <summary>
        /// The keys the authority accepts, as a list - <c>CloudLogin:ServiceKeys:0</c> and so on,
        /// which is the shape <c>CloudLoginWebConfiguration.ServiceKeys</c> binds from. A singular
        /// key here binds to nothing and leaves the authority rejecting every service call.
        /// </summary>
        public const string AuthorityKeys = "CloudLogin:ServiceKeys";
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
