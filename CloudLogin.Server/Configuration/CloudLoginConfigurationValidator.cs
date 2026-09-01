using AngryMonkey.CloudLogin.Sever.Providers;
using System.Text.RegularExpressions;

namespace AngryMonkey.CloudLogin.Server;

public static partial class CloudLoginConfigurationValidator
{
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*$")]
    private static partial Regex UriSchemePattern();

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColorPattern();

    public static void Validate(CloudLoginWebConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Security);

        // Resolve version-implied defaults before anything reads them, so every host - standalone,
        // embedded, and the Aspire hosting integration - sees the same state.
        configuration.NormalizeVersions();

        PrepareMicrosoftProvider(configuration, isDevelopment);


        CloudLoginSecurityOptions security = configuration.Security;

        if (!isDevelopment && !security.RequireHttps)
            throw new InvalidOperationException("CloudLogin HTTPS enforcement cannot be disabled outside Development.");

        if (configuration.LoginDuration <= TimeSpan.Zero || configuration.LoginDuration > TimeSpan.FromDays(90))
            throw new InvalidOperationException("LoginDuration must be greater than zero and no longer than 90 days.");

        if (security.SessionIdleTimeout <= TimeSpan.Zero || security.SessionIdleTimeout > TimeSpan.FromDays(1))
            throw new InvalidOperationException("Security.SessionIdleTimeout must be greater than zero and no longer than 24 hours.");

        if (security.MinimumPasswordLength < 8 ||
            security.MaximumPasswordLength < security.MinimumPasswordLength ||
            security.MaximumPasswordLength > 1024)
            throw new InvalidOperationException("Password length limits are invalid.");

        if (security.PasswordHashIterations < CloudLoginSecurityOptions.MinimumPbkdf2Iterations)
            throw new InvalidOperationException($"PasswordHashIterations must be at least {CloudLoginSecurityOptions.MinimumPbkdf2Iterations:N0}.");

        if (security.AuthenticationPermitLimit <= 0 || security.AuthenticationWindow <= TimeSpan.Zero)
            throw new InvalidOperationException("Authentication rate-limit settings must be greater than zero.");

        if (security.MaximumProfileImageBytes < 1024 || security.MaximumProfileImageBytes > 20 * 1024 * 1024)
            throw new InvalidOperationException("MaximumProfileImageBytes must be between 1 KB and 20 MB.");

        if (string.IsNullOrWhiteSpace(configuration.CookieName))
            throw new InvalidOperationException("CookieName is required.");

        if (string.IsNullOrWhiteSpace(configuration.PrimaryColor) || !HexColorPattern().IsMatch(configuration.PrimaryColor))
            throw new InvalidOperationException("PrimaryColor must be a hex color, e.g. \"#0078D4\" or \"#06C\".");

        if (!string.IsNullOrWhiteSpace(configuration.CookieDomain) &&
            configuration.CookieName.StartsWith("__Host-", StringComparison.Ordinal))
            throw new InvalidOperationException("A __Host- cookie cannot specify CookieDomain. Change CookieName only when domain-wide cookies are explicitly required.");

        foreach (string origin in configuration.AllowedRedirectOrigins)
            ValidateOrigin(origin);

        foreach (string serviceKey in configuration.ServiceKeys)
        {
            if (string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Trim().Length < 32)
                throw new InvalidOperationException("Each ServiceKeys entry must be at least 32 characters — generate one with a cryptographically random secret, not a guessable string.");
        }

        foreach (CloudLoginWebhookRegistration webhook in configuration.Webhooks)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(webhook.Application);
            
            if (!webhook.Url.IsAbsoluteUri
                || (!isDevelopment && webhook.Url.Scheme != Uri.UriSchemeHttps)
                || (isDevelopment && webhook.Url.Scheme != Uri.UriSchemeHttp
                    && webhook.Url.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Webhook URLs must be absolute HTTPS URLs (HTTP is allowed only in development).");
            
            if (string.IsNullOrWhiteSpace(webhook.Secret) || webhook.Secret.Length < 32)
                throw new InvalidOperationException("Webhook secrets must contain at least 32 characters.");
            
            if (webhook.Events.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Webhook event identifiers cannot be empty.");
        }

        foreach (string scheme in configuration.AllowedMobileSchemes)
        {
            if (!UriSchemePattern().IsMatch(scheme) ||
                scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Allowed mobile scheme '{scheme}' is invalid.");
        }

        bool hasLegacyCodeProvider = configuration.Providers.Any(provider =>
            provider.IsCodeVerification && !provider.IsExternal);

        if (hasLegacyCodeProvider && !security.EnableLegacyClientVerificationCodes)
            throw new InvalidOperationException(
                "A client-managed verification-code provider is configured. This legacy flow is disabled by default because verification occurs in browser code.");

        if (security.EnableLegacyClientVerificationCodes && !isDevelopment)
            throw new InvalidOperationException("Legacy client-managed verification codes cannot be enabled outside Development.");

        ValidateApiVersion(configuration);
        ValidateDatabaseVersion(configuration);
        ValidateSignInProfiles(configuration);
        ValidateCore(configuration);
    }

    private static void ValidateApiVersion(CloudLoginWebConfiguration configuration)
    {
        if (!Enum.IsDefined(configuration.ApiVersion))
            throw new InvalidOperationException($"CloudLogin ApiVersion '{configuration.ApiVersion}' is invalid.");
    }

    private static void ValidateDatabaseVersion(CloudLoginWebConfiguration configuration)
    {
        if (!Enum.IsDefined(configuration.DatabaseVersion))
            throw new InvalidOperationException($"CloudLogin DatabaseVersion '{configuration.DatabaseVersion}' is invalid.");

        CosmosConfiguration cosmos = configuration.Cosmos;

        if (configuration.DatabaseVersion == Versioning.CloudLoginDatabaseVersion.V2)
        {
            // V2 reads a database an earlier CloudLogin wrote, so it has to be named; there is no
            // sensible default that would not risk pointing at the wrong existing data.
            if (string.IsNullOrWhiteSpace(cosmos.DatabaseId) || string.IsNullOrWhiteSpace(cosmos.ContainerId))
                throw new InvalidOperationException(
                    "DatabaseVersion V2 reads an existing database, so Cosmos:DatabaseId and Cosmos:ContainerId must both name it. " +
                    "Remove the DatabaseVersion setting to use V3 instead, which creates its own database.");

            if (configuration.CoreExplicitlyConfigured)
                throw new InvalidOperationException(
                    "Core settings configure the V3 storage model but DatabaseVersion is V2. " +
                    "Either remove the Core settings, or set DatabaseVersion to V3.");

            return;
        }

        // V3 carries no legacy compatibility, so the legacy schema knobs would silently do
        // nothing. Fail loudly rather than let a deployment believe they are in effect.
        List<string> legacySettings = [];

        if (cosmos.IncludeLegacySchema)
            legacySettings.Add(nameof(cosmos.IncludeLegacySchema));

        if (cosmos.SaveIdMode != IdSaveMode.Raw)
            legacySettings.Add(nameof(cosmos.SaveIdMode));

        if (!string.IsNullOrWhiteSpace(cosmos.UserInfoPartitionKeyValue))
            legacySettings.Add(nameof(cosmos.UserInfoPartitionKeyValue));

        if (cosmos.JsonCompatibilityMode != JsonCompatibilityMode.Standard)
            legacySettings.Add(nameof(cosmos.JsonCompatibilityMode));

        if (legacySettings.Count > 0)
            throw new InvalidOperationException(
                $"Cosmos legacy compatibility settings ({string.Join(", ", legacySettings)}) apply to DatabaseVersion V2 only; " +
                "the V3 storage model has no legacy schema. Either remove them, or set DatabaseVersion to V2.");
    }

    private static void ValidateSignInProfiles(CloudLoginWebConfiguration configuration)
    {
        Core.Application.SignInProfileConfiguration profiles = configuration.SignInProfiles;

        if (profiles.Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
            throw new InvalidOperationException("Every sign-in profile requires a name.");

        // Configuration binding appends list items onto the seeded defaults, so a projected
        // configuration can deliver the same profile twice. Structurally identical duplicates
        // collapse silently; same name with different content is a real conflict.
        profiles.Profiles = [.. profiles.Profiles
            .DistinctBy(profile =>
                (profile.Name.ToLowerInvariant(),
                 string.Join("|", profile.VisibleMethods.Select(method => method.ToLowerInvariant())),
                 string.Join("|", profile.AllowedMethods.Select(method => method.ToLowerInvariant()))))];

        List<string> duplicateNames = [.. profiles.Profiles
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        if (duplicateNames.Count > 0)
            throw new InvalidOperationException(
                $"Sign-in profiles named more than once with different settings: {string.Join(", ", duplicateNames)}.");

        if (!profiles.Profiles.Any(profile => string.Equals(profile.Name, profiles.DefaultProfile, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"SignInProfiles.DefaultProfile '{profiles.DefaultProfile}' is not a configured profile.");

        foreach ((string client, List<string> allowed) in profiles.ClientProfiles)
            foreach (string profileName in allowed)
                if (!profiles.Profiles.Any(profile => string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        $"Client '{client}' allows sign-in profile '{profileName}', which is not configured.");
    }

    private static void ValidateCore(CloudLoginWebConfiguration configuration)
    {
        Core.CloudLoginCoreConfiguration? core = configuration.Core;

        if (core is null)
            return;

        if (string.IsNullOrWhiteSpace(core.RealmId))
            throw new InvalidOperationException("Core.RealmId is required.");

        if (string.IsNullOrWhiteSpace(core.DatabaseId))
            throw new InvalidOperationException("Core.DatabaseId is required.");

        // A host with no Cosmos account is running on its own ICloudLoginStore (demos, tests, an
        // in-memory harness), so the Azure-backed core is not in play and nothing below applies.
        if (!configuration.Cosmos.IsValid())
            return;

        if (configuration.AzureStorage is null || !configuration.AzureStorage.IsValid())
            throw new InvalidOperationException(
                "The CloudLogin core (database version V3) requires Azure Storage: the IdentityKeys table is the " +
                "identity index every sign-in resolves through. Configure the Storage section, or select " +
                "DatabaseVersion V2 to keep the legacy single-container store.");

        ValidateRealmDatabaseIsolation(configuration, core);

        // Throws with an actionable message when the secret is missing, too short, malformed, or
        // visibly not random. Checked here rather than at first use so a misconfigured deployment
        // fails at startup instead of at the first sign-in, and only once Azure Storage is in play
        // so V1/V2 and in-memory hosts never need the setting.
        Core.Domain.IdentityKeyHasher.FromConfiguredSecrets(
            configuration.IdentityHmacSecret,
            configuration.IdentityHmacFallbackSecrets);

        if (core.InvitationLifetime <= TimeSpan.Zero || core.AuditRetention <= TimeSpan.Zero ||
            core.SessionFamilyLifetime <= TimeSpan.Zero || core.RefreshTokenLifetime <= TimeSpan.Zero ||
            core.LoginRequestLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Core lifetimes must all be positive.");

        if (core.RefreshTokenLifetime > core.SessionFamilyLifetime)
            throw new InvalidOperationException("Core.RefreshTokenLifetime cannot exceed Core.SessionFamilyLifetime.");

        Core.DeviceAuthorizationConfiguration device = core.DeviceAuthorization;

        if (device.CodeLifetime <= TimeSpan.Zero || device.PollIntervalSeconds <= 0 ||
            device.MaxPollViolations <= 0 || device.UserCodeLength < 6)
            throw new InvalidOperationException(
                "Device authorization settings are invalid: lifetime and poll interval must be positive, and the user code needs at least 6 characters.");

        if (device.Clients.Any(client =>
                string.IsNullOrWhiteSpace(client.Key) || string.IsNullOrWhiteSpace(client.Value)))
            throw new InvalidOperationException(
                "Every Core.DeviceAuthorization.Clients entry requires a client id and trusted description.");
    }

    /// <summary>
    /// One Cosmos database per realm, and never the legacy database.
    /// <para>
    /// A realm is the isolation boundary between two authorities that happen to share Azure
    /// resources. Sharing one database across realms would put both realms' users in the same
    /// <c>Users</c> container with no discriminator at all — every cross-realm read would
    /// succeed, which is worse than it failing. And a realm that shares the *default* database
    /// name is indistinguishable from the default realm, so a second realm has to name its own.
    /// </para>
    /// </summary>
    private static void ValidateRealmDatabaseIsolation(
        CloudLoginWebConfiguration configuration, Core.CloudLoginCoreConfiguration core)
    {
        // An unnamed database resolves from the realm, so it is correct by construction. Only an
        // explicitly named one can be wrong, and there are exactly two ways: naming the database
        // another realm would resolve to, or naming the legacy V2 database.
        if (core.DatabaseIdExplicitlyConfigured)
        {
            string expected = Core.CloudLoginCoreContainers.DatabaseIdFor(core.RealmId);

            if (!string.Equals(core.DatabaseId, expected, StringComparison.OrdinalIgnoreCase))
            {
                // The realm-derived names form a reserved namespace. A hand-picked name that lands
                // inside it belongs to a different realm - two realms would then share containers
                // with nothing separating their users, and every cross-realm read would succeed.
                if (core.DatabaseId.StartsWith(Core.CloudLoginCoreContainers.DefaultDatabaseId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Core.DatabaseId '{core.DatabaseId}' is inside the namespace CloudLogin derives per-realm " +
                        $"database names from. Realm '{core.RealmId}' resolves to '{expected}'. Leave " +
                        "Core.DatabaseId unset to get that automatically, or choose a name that does not start " +
                        $"with '{Core.CloudLoginCoreContainers.DefaultDatabaseId}'.");
            }
        }

        // The legacy V2 container lives in its own database. Pointing the core at that same
        // database would create the seven core containers alongside it and leave two models
        // writing into one place.
        if (!string.IsNullOrWhiteSpace(configuration.Cosmos.DatabaseId) &&
            string.Equals(core.DatabaseId, configuration.Cosmos.DatabaseId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Core.DatabaseId and Cosmos:DatabaseId both name '{core.DatabaseId}'. The V3 core database is " +
                "separate from the legacy V2 database by design; give the core its own name, or remove " +
                "Cosmos:DatabaseId if this deployment has no legacy database to read.");
    }

    private static void PrepareMicrosoftProvider(
        CloudLoginWebConfiguration configuration,
        bool isDevelopment)
    {
        LoginProviders.MicrosoftProviderConfiguration? microsoft = configuration.Providers
            .OfType<LoginProviders.MicrosoftProviderConfiguration>()
            .FirstOrDefault();

        if (microsoft is null)
            return;

        bool hasClientId = !string.IsNullOrWhiteSpace(microsoft.ClientId);
        bool hasSecret = !string.IsNullOrWhiteSpace(microsoft.ClientSecret);
        bool hasCertificate = microsoft.VaultEndpoint is not null &&
            !string.IsNullOrWhiteSpace(microsoft.CertificateName);

        if (hasClientId && (hasSecret || hasCertificate))
            return;

        if (!isDevelopment)
            throw new InvalidOperationException(
                "Microsoft sign-in requires ClientId and either ClientSecret or both VaultEndpoint and CertificateName.");

        configuration.Providers.Remove(microsoft);
    }

    private static void ValidateOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)))
            throw new InvalidOperationException($"Allowed redirect origin '{origin}' must be an HTTP(S) origin without a path, query, credentials, or fragment.");

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException($"Allowed redirect origin '{origin}' must use HTTPS unless it is loopback development traffic.");
    }
}
