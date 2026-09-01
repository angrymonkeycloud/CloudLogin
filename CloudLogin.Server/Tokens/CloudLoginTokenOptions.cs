using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Issuer-side token policy. The defaults are the recommended values; every one of
/// them trades convenience against blast radius, so they are documented rather than
/// left as bare numbers.
/// </summary>
public sealed class CloudLoginTokenOptions
{
    /// <summary>
    /// The "iss" claim and the base of the discovery document. Must be the public
    /// HTTPS origin of the authority, because resource servers pin against it.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Audiences the authority will mint tokens for. A resource server is identified
    /// by its audience string, and a token minted for one audience must not be
    /// accepted by another &mdash; that is what stops a compromised low-value service
    /// from replaying a user's token against a high-value one.
    /// </summary>
    public HashSet<string> AllowedAudiences { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Access-token lifetime. Short by design: an access token cannot be revoked
    /// once minted, so its lifetime <em>is</em> the revocation window.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Refresh-token lifetime. Long lived, but revocable and single-use, so the
    /// exposure from a leaked one is bounded by reuse detection.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>How long a signing key is used to sign before the next key takes over.</summary>
    public TimeSpan SigningKeyRotationInterval { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a retired key stays published for verification after it stops signing.
    /// Must comfortably exceed <see cref="AccessTokenLifetime"/> so rotation never
    /// invalidates tokens that are still in flight.
    /// </summary>
    public TimeSpan SigningKeyPublishGrace { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Tolerance for clock drift between the authority and resource servers.
    /// Kept deliberately tight; the ASP.NET default of five minutes is far more
    /// than modern time sync needs and needlessly extends a token's usable life.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registered service clients allowed to perform delegated token exchange.
    /// Keyed by client id; the value is the set of audiences that client may
    /// request a delegated token for.
    /// </summary>
    public Dictionary<string, CloudLoginServiceClient> ServiceClients { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>Where the authority's signing keys live: Key Vault, or the Cosmos fallback.</summary>
    public CloudLoginSigningKeyStoreOptions SigningKeys { get; set; } = new();

    internal void Validate()
    {
        SigningKeys.Validate();

        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException(
                "CloudLogin token issuer requires an Issuer. Set it to the authority's public HTTPS origin.");

        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out Uri? issuerUri))
            throw new InvalidOperationException($"CloudLogin token Issuer '{Issuer}' is not an absolute URI.");

        if (issuerUri.Scheme != Uri.UriSchemeHttps && !issuerUri.IsLoopback)
            throw new InvalidOperationException(
                "CloudLogin token Issuer must use HTTPS outside of loopback development.");

        if (AccessTokenLifetime <= TimeSpan.Zero || AccessTokenLifetime > TimeSpan.FromHours(1))
            throw new InvalidOperationException(
                "AccessTokenLifetime must be positive and no more than one hour. Access tokens cannot be revoked, so a long lifetime is a long window of unstoppable access.");

        if (SigningKeyPublishGrace <= AccessTokenLifetime)
            throw new InvalidOperationException(
                "SigningKeyPublishGrace must exceed AccessTokenLifetime, otherwise key rotation invalidates tokens that are still valid.");

        foreach ((string key, CloudLoginServiceClient client) in ServiceClients)
        {
            if (string.IsNullOrWhiteSpace(client.ClientId))
                client.ClientId = key;

            client.NormalizeSecret();
        }
    }
}

/// <summary>
/// Where the ES256 signing keys live.
/// <para>
/// Production deployments should use Azure Key Vault or Managed HSM
/// (<see cref="KeyVaultKeyId"/>): the private key is created non-exportable and every signature
/// is computed inside the vault, so no process memory or database ever holds material that can
/// mint tokens. The Cosmos <c>SigningKeys</c> container remains available as a fallback — its
/// private keys are Data Protection-wrapped and retire through TTL — but a modernized (Core)
/// production deployment must choose explicitly: configure the vault key, or opt in to the
/// fallback with <see cref="AllowCosmosFallback"/>.
/// </para>
/// </summary>
public sealed class CloudLoginSigningKeyStoreOptions
{
    /// <summary>
    /// The Key Vault key identifier, for example
    /// <c>https://myvault.vault.azure.net/keys/cloudlogin-signing</c>. The key must be an EC
    /// P-256 key with the Sign operation permitted; create it non-exportable and rotate it with
    /// a vault rotation policy.
    /// </summary>
    public Uri? KeyVaultKeyId { get; set; }

    /// <summary>
    /// The credential for Key Vault. Set in code (a credential is an object, not a value);
    /// defaults to <c>DefaultAzureCredential</c> when a vault key is configured.
    /// </summary>
    public global::Azure.Core.TokenCredential? KeyVaultCredential { get; set; }

    /// <summary>
    /// Explicit opt-in to the Cosmos SigningKeys fallback. Left unset, legacy deployments keep
    /// their current behavior; a Core-enabled production deployment fails startup until it
    /// either configures <see cref="KeyVaultKeyId"/> or sets this to true deliberately.
    /// </summary>
    public bool? AllowCosmosFallback { get; set; }

    /// <summary>
    /// Turns the store choice above from a recommendation into a startup check. Off by default:
    /// the Cosmos fallback is Data Protection-wrapped and TTL-retired, so it is a supported
    /// production configuration, and the V3 storage model is every deployment's default rather
    /// than something opted into - demanding a choice by default would fail startup for anyone
    /// who simply took the defaults. Set this in a deployment whose policy requires a vault key.
    /// </summary>
    public bool RequireExplicitStoreChoice { get; set; }

    internal void Validate()
    {
        if (RequireExplicitStoreChoice && KeyVaultKeyId is null && AllowCosmosFallback != true)
            throw new InvalidOperationException(
                "CloudLogin core deployments must choose a production signing-key store: set " +
                "CloudLoginTokens:SigningKeys:KeyVaultKeyId to a Key Vault key, or explicitly set " +
                "CloudLoginTokens:SigningKeys:AllowCosmosFallback to true to keep the encrypted Cosmos fallback.");
    }
}

/// <summary>
/// A backend service permitted to call the authority on a user's behalf.
/// The secret authenticates the <em>service</em>; the subject token it presents
/// authenticates the <em>user</em>. Both are required, so neither a stolen secret
/// nor a stolen user token is sufficient on its own.
/// </summary>
public sealed class CloudLoginServiceClient
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the client secret, base64 encoded. Validated runtime options use this value
    /// and do not retain the bound plaintext value.
    /// </summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>
    /// A secret supplied by a protected configuration provider. Options validation converts it to
    /// <see cref="SecretHash"/> and clears this bound property, allowing an Aspire AppHost to give
    /// the same generated value to both sides without storing it in source or a manifest.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>Audiences this client may request delegated tokens for.</summary>
    public HashSet<string> AllowedAudiences { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The audience of tokens this client legitimately receives &mdash; normally its
    /// own audience.
    /// <para>
    /// Token exchange validates the presented subject token against this value, so a
    /// service can only delegate tokens that were actually issued <em>to it</em>. Without
    /// it, a service that got hold of a token minted for a different service could
    /// exchange that token and act on the user's behalf somewhere it was never given
    /// access.
    /// </para>
    /// </summary>
    public string? Audience { get; set; }

    public string? DisplayName { get; set; }

    public bool IsDisabled { get; set; }

    internal void NormalizeSecret()
    {
        if (!string.IsNullOrWhiteSpace(ClientSecret))
        {
            SecretHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret)));
            ClientSecret = null;
        }

        if (string.IsNullOrWhiteSpace(SecretHash))
            throw new InvalidOperationException($"CloudLogin service client '{ClientId}' requires a secret or secret hash.");
    }
}
