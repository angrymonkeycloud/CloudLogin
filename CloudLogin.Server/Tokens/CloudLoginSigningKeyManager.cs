using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// Owns the authority's ES256 signing keys: generation, rotation, and publication
/// through JWKS.
/// <para>
/// ES256 is used rather than a shared HMAC secret because verification must not
/// require the ability to sign. Resource servers fetch the public key from JWKS and
/// can validate tokens without holding any credential that would let them mint one
/// &mdash; so a compromised resource server cannot forge identities.
/// </para>
/// </summary>
public sealed class CloudLoginSigningKeyManager
{
    private const string ProtectorPurpose = "AngryMonkey.CloudLogin.Tokens.SigningKey.v1";

    private readonly ICloudLoginTokenStore _store;
    private readonly IDataProtector _protector;
    private readonly CloudLoginTokenOptions _options;
    private readonly ILogger<CloudLoginSigningKeyManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<CloudLoginSigningKey> _cached = [];
    private DateTimeOffset _cacheExpiresOn = DateTimeOffset.MinValue;

    public CloudLoginSigningKeyManager(
        ICloudLoginTokenStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<CloudLoginTokenOptions> options,
        ILogger<CloudLoginSigningKeyManager> logger)
    {
        _store = store;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the key new tokens should be signed with, generating or rotating one
    /// if the current key has aged out.
    /// </summary>
    public async Task<(SigningCredentials Credentials, string KeyId)> GetSigningCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<CloudLoginSigningKey> keys = await GetKeysAsync(cancellationToken);
        CloudLoginSigningKey? active = keys
            .Where(key => key.CanSign(now))
            .OrderByDescending(key => key.CreatedOn)
            .FirstOrDefault();

        active ??= await RotateAsync(cancellationToken);

        ECDsa privateKey = ImportPrivateKey(active);
        ECDsaSecurityKey securityKey = new(privateKey) { KeyId = active.KeyId };

        return (new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256), active.KeyId);
    }

    /// <summary>
    /// Every key currently trusted for verification &mdash; the active signer plus any
    /// retired keys still inside their publication grace window.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> GetValidationKeysAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<CloudLoginSigningKey> keys = await GetKeysAsync(cancellationToken);

        return
        [
            .. keys
                .Where(key => key.CanVerify(now))
                .Select(key => (SecurityKey)new ECDsaSecurityKey(ImportPublicKey(key)) { KeyId = key.KeyId })
        ];
    }

    /// <summary>
    /// Builds the JWKS document. Only public components are ever included; the
    /// serializer would happily emit "d" if it were present, so the key is imported
    /// from its public coordinates rather than unwrapped from the private material.
    /// </summary>
    public async Task<object> GetJsonWebKeySetAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<CloudLoginSigningKey> keys = await GetKeysAsync(cancellationToken);

        return new
        {
            keys = keys
                .Where(key => key.CanVerify(now))
                .OrderByDescending(key => key.CreatedOn)
                .Select(key => new
                {
                    kty = "EC",
                    use = "sig",
                    alg = "ES256",
                    crv = "P-256",
                    kid = key.KeyId,
                    x = key.PublicX,
                    y = key.PublicY
                })
                .ToArray()
        };
    }

    /// <summary>
    /// Generates a new signing key and retires the outgoing one. Retired keys keep
    /// verifying for <see cref="CloudLoginTokenOptions.SigningKeyPublishGrace"/> so
    /// tokens already in flight are unaffected.
    /// </summary>
    public async Task<CloudLoginSigningKey> RotateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // Re-check under the lock: a concurrent request may have rotated already,
            // and minting two keys would leave one orphaned in JWKS.
            IReadOnlyList<CloudLoginSigningKey> existing = await LoadAsync(cancellationToken);
            CloudLoginSigningKey? active = existing
                .Where(key => key.CanSign(now))
                .OrderByDescending(key => key.CreatedOn)
                .FirstOrDefault();

            if (active is not null)
                return active;

            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ECParameters parameters = ecdsa.ExportParameters(includePrivateParameters: true);
            byte[] pkcs8 = ecdsa.ExportPkcs8PrivateKey();

            CloudLoginSigningKey key = new()
            {
                KeyId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16)),
                ProtectedPrivateKey = Convert.ToBase64String(_protector.Protect(pkcs8)),
                PublicX = Base64UrlEncoder.Encode(parameters.Q.X!),
                PublicY = Base64UrlEncoder.Encode(parameters.Q.Y!),
                CreatedOn = now,
                SigningExpiresOn = now.Add(_options.SigningKeyRotationInterval),
                PublishExpiresOn = now
                    .Add(_options.SigningKeyRotationInterval)
                    .Add(_options.SigningKeyPublishGrace)
            };

            key.SetId(Guid.NewGuid());
            key.ttl = (int)(key.PublishExpiresOn - now).TotalSeconds + (int)TimeSpan.FromDays(1).TotalSeconds;

            CryptographicOperations.ZeroMemory(pkcs8);

            await _store.SaveSigningKeyAsync(key, cancellationToken);
            InvalidateCache();

            _logger.LogInformation(
                "CloudLogin rotated its token signing key. New kid {KeyId} signs until {SigningExpiresOn:o}.",
                key.KeyId,
                key.SigningExpiresOn);

            return key;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<CloudLoginSigningKey>> GetKeysAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _cacheExpiresOn && _cached.Count > 0)
            return _cached;

        return await LoadAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CloudLoginSigningKey>> LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CloudLoginSigningKey> keys = await _store.GetSigningKeysAsync(cancellationToken);

        _cached = keys;

        // Short cache: long enough to keep JWKS and signing off the hot path, short
        // enough that a rotation on another instance is picked up quickly.
        _cacheExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5);

        return keys;
    }

    private void InvalidateCache() => _cacheExpiresOn = DateTimeOffset.MinValue;

    private ECDsa ImportPrivateKey(CloudLoginSigningKey key)
    {
        byte[] pkcs8 = _protector.Unprotect(Convert.FromBase64String(key.ProtectedPrivateKey));

        try
        {
            ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
            return ecdsa;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    private static ECDsa ImportPublicKey(CloudLoginSigningKey key) =>
        ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64UrlEncoder.DecodeBytes(key.PublicX),
                Y = Base64UrlEncoder.DecodeBytes(key.PublicY)
            }
        });
}
