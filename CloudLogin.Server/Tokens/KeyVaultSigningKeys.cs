using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// ES256 signing through Azure Key Vault / Managed HSM. The private key never leaves the vault:
/// tokens are signed by <see cref="CryptographyClient"/> calls, verification and JWKS use only
/// the public coordinates, and rotation is the vault's own key-version rotation — the newest
/// enabled version signs while every still-enabled version keeps verifying.
/// </summary>
public sealed class KeyVaultSigningKeyProvider
{
    private readonly KeyClient _keyClient;
    private readonly string _keyName;
    private readonly global::Azure.Core.TokenCredential _credential;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<KeyVaultKey> _cachedVersions = [];
    private DateTimeOffset _cacheExpiresOn = DateTimeOffset.MinValue;

    public KeyVaultSigningKeyProvider(CloudLoginSigningKeyStoreOptions options)
    {
        Uri keyId = options.KeyVaultKeyId
            ?? throw new InvalidOperationException("KeyVaultSigningKeyProvider requires SigningKeys.KeyVaultKeyId.");

        // A Key Vault key id is https://{vault}/keys/{name}[/{version}].
        Uri vaultUri = new(keyId.GetLeftPart(UriPartial.Authority));
        string[] segments = keyId.AbsolutePath.Trim('/').Split('/');

        if (segments.Length < 2 || !string.Equals(segments[0], "keys", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"SigningKeys.KeyVaultKeyId '{keyId}' is not a Key Vault key identifier (expected https://<vault>/keys/<name>).");

        _keyName = segments[1];
        _credential = options.KeyVaultCredential ?? new DefaultAzureCredential();
        _keyClient = new KeyClient(vaultUri, _credential);
    }

    public async Task<(SigningCredentials Credentials, string KeyId)> GetSigningCredentialsAsync(CancellationToken cancellationToken = default)
    {
        KeyVaultKey signingKey = await GetNewestEnabledVersionAsync(cancellationToken);
        string kid = signingKey.Properties.Version;

        ECDsaSecurityKey securityKey = new(ImportPublicKey(signingKey)) { KeyId = kid };
        CryptographyClient cryptography = new(signingKey.Id, _credential);

        KeyVaultCryptoProviderFactory factory = new(cryptography);
        securityKey.CryptoProviderFactory = factory;

        return (new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256) { CryptoProviderFactory = factory }, kid);
    }

    public async Task<IReadOnlyList<SecurityKey>> GetValidationKeysAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KeyVaultKey> versions = await GetEnabledVersionsAsync(cancellationToken);

        return
        [
            .. versions.Select(version => (SecurityKey)new ECDsaSecurityKey(ImportPublicKey(version))
            {
                KeyId = version.Properties.Version
            })
        ];
    }

    public async Task<object> GetJsonWebKeySetAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KeyVaultKey> versions = await GetEnabledVersionsAsync(cancellationToken);

        return new
        {
            keys = versions
                .OrderByDescending(version => version.Properties.CreatedOn)
                .Select(version => new
                {
                    kty = "EC",
                    use = "sig",
                    alg = "ES256",
                    crv = "P-256",
                    kid = version.Properties.Version,
                    x = Base64UrlEncoder.Encode(version.Key.X),
                    y = Base64UrlEncoder.Encode(version.Key.Y)
                })
                .ToArray()
        };
    }

    private async Task<KeyVaultKey> GetNewestEnabledVersionAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<KeyVaultKey> versions = await GetEnabledVersionsAsync(cancellationToken);

        return versions
            .OrderByDescending(version => version.Properties.CreatedOn)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Key Vault key '{_keyName}' has no enabled version usable for signing.");
    }

    private async Task<IReadOnlyList<KeyVaultKey>> GetEnabledVersionsAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _cacheExpiresOn && _cachedVersions.Count > 0)
            return _cachedVersions;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresOn && _cachedVersions.Count > 0)
                return _cachedVersions;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<KeyVaultKey> versions = [];

            await foreach (KeyProperties properties in _keyClient.GetPropertiesOfKeyVersionsAsync(_keyName, cancellationToken))
            {
                if (properties.Enabled != true)
                    continue;

                if (properties.NotBefore is { } notBefore && notBefore > now)
                    continue;

                if (properties.ExpiresOn is { } expiresOn && expiresOn <= now)
                    continue;

                KeyVaultKey key = await _keyClient.GetKeyAsync(_keyName, properties.Version, cancellationToken);

                if (key.KeyType == KeyType.Ec || key.KeyType == KeyType.EcHsm)
                    versions.Add(key);
            }

            _cachedVersions = versions;
            _cacheExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5);
            return versions;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ECDsa ImportPublicKey(KeyVaultKey key) =>
        ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = key.Key.X, Y = key.Key.Y }
        });
}

/// <summary>
/// Routes signing for a Key Vault-backed key through <see cref="CryptographyClient"/>;
/// everything else (verification with the public key) falls back to the default providers.
/// </summary>
internal sealed class KeyVaultCryptoProviderFactory(CryptographyClient cryptography) : CryptoProviderFactory
{
    private readonly CryptographyClient _cryptography = cryptography;

    public override SignatureProvider CreateForSigning(SecurityKey key, string algorithm) =>
        new KeyVaultSignatureProvider(_cryptography, key, algorithm);
}

internal sealed class KeyVaultSignatureProvider(CryptographyClient cryptography, SecurityKey key, string algorithm)
    : SignatureProvider(key, algorithm)
{
    private readonly CryptographyClient _cryptography = cryptography;

    public override byte[] Sign(byte[] input)
    {
        // Key Vault signs a precomputed digest and returns the raw r||s signature JWTs need.
        byte[] digest = SHA256.HashData(input);
        return _cryptography.Sign(SignatureAlgorithm.ES256, digest).Signature;
    }

    public override bool Verify(byte[] input, byte[] signature)
    {
        byte[] digest = SHA256.HashData(input);
        return _cryptography.Verify(SignatureAlgorithm.ES256, digest, signature).IsValid;
    }

    protected override void Dispose(bool disposing)
    {
    }
}
