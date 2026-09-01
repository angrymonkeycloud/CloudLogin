using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>
/// Thrown at startup when <c>CloudLogin:IdentityHmacSecret</c> is missing, malformed, or too weak.
/// Identity resolution is keyed on this secret, so a deployment that cannot produce a usable one
/// must not start: there is no safe fallback, and running on a substitute key would silently
/// orphan every account already written.
/// </summary>
public sealed class IdentityHmacSecretException(string message) : InvalidOperationException(message);

/// <summary>
/// Keys the identity index with HMAC-SHA256.
/// <para>
/// A plain SHA-256 of <c>email:ada@example.com</c> is offline-computable by anyone who reads the
/// table, so the index would be a confirmation oracle for "does this address have an account
/// here" against a dictionary of addresses. Keying the hash removes that: without the secret the
/// row keys are meaningless, and the index still resolves in one point lookup because the same
/// secret is applied on every write and every read.
/// </para>
/// <para>
/// The primary key is authoritative and writes every new row. Optional fallback keys are read-only
/// compatibility keys for a deliberate change. A fallback hit is re-keyed by the identity store,
/// keeping existing accounts reachable while storage converges on the primary key.
/// </para>
/// <para>
/// The value is never logged, never echoed into an exception message, and never returned by any
/// API — see the error text below, which describes shape and length only.
/// </para>
/// </summary>
public sealed class IdentityKeyHasher
{
    /// <summary>Configuration key holding the secret, in its logical (colon-separated) form.</summary>
    public const string ConfigurationKey = "CloudLogin:IdentityHmacSecret";

    /// <summary>
    /// The portable environment-variable spelling of <see cref="ConfigurationKey"/>. Double
    /// underscores rather than a colon because that is the form that survives every host: Linux
    /// App Service, containers, and shells that will not accept a colon in a variable name.
    /// </summary>
    public const string EnvironmentVariableName = "CloudLogin__IdentityHmacSecret";

    /// <summary>One JSON-array setting holding old read-only keys during a deliberate rotation.</summary>
    public const string FallbackConfigurationKey = "CloudLogin:IdentityHmacFallbackSecrets";

    /// <summary>The portable environment-variable spelling of <see cref="FallbackConfigurationKey"/>.</summary>
    public const string FallbackEnvironmentVariableName = "CloudLogin__IdentityHmacFallbackSecrets";

    /// <summary>The minimum accepted key length. Below the HMAC-SHA256 block-equivalent strength there is no point keying at all.</summary>
    public const int MinimumSecretBytes = 32;

    /// <summary>Bumped only if the keying construction itself changes; recorded on every row so a future change is detectable.</summary>
    public const int CurrentHashVersion = 1;

    /// <summary>Bumped only if <see cref="Application.IdentityNormalization"/> changes what it produces for the same identity.</summary>
    public const int CurrentNormalizationVersion = 1;

    private readonly byte[] _key;
    private readonly byte[][] _fallbackKeys;

    private IdentityKeyHasher(byte[] key, byte[][] fallbackKeys)
    {
        _key = key;
        _fallbackKeys = fallbackKeys;
    }

    /// <summary>HMAC-SHA256 of the canonical identity string, as 64 lowercase hex characters.</summary>
    public string ComputeHash(string canonicalValue) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(canonicalValue)));

    /// <summary>
    /// Returns the primary hash first, followed by hashes under each configured fallback key.
    /// Keys themselves are never exposed.
    /// </summary>
    public IReadOnlyList<string> ComputeCandidateHashes(string canonicalValue)
    {
        byte[] value = Encoding.UTF8.GetBytes(canonicalValue);
        string[] hashes = new string[_fallbackKeys.Length + 1];
        hashes[0] = Convert.ToHexStringLower(HMACSHA256.HashData(_key, value));

        for (int index = 0; index < _fallbackKeys.Length; index++)
            hashes[index + 1] = Convert.ToHexStringLower(HMACSHA256.HashData(_fallbackKeys[index], value));

        return hashes;
    }

    /// <summary>
    /// The two-character bucket in the Table Storage partition key, spreading one identity type
    /// over 256 partitions while keeping every lookup a deterministic point read.
    /// </summary>
    public static string Bucket(string fullHash) => fullHash[..2];

    /// <summary>
    /// Builds the hasher from the configured secret, or throws.
    /// <para>
    /// Accepts base64 or hex; both must decode to at least <see cref="MinimumSecretBytes"/> bytes.
    /// </para>
    /// </summary>
    public static IdentityKeyHasher FromConfiguredSecret(string? configuredSecret) =>
        FromConfiguredSecrets(configuredSecret, null);

    /// <summary>
    /// Builds the hasher from one primary secret and optional old read-only secrets. The fallback
    /// order is preserved and duplicate key material is rejected as a configuration mistake.
    /// </summary>
    public static IdentityKeyHasher FromConfiguredSecrets(
        string? configuredSecret,
        IEnumerable<string>? configuredFallbackSecrets)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret))
            throw new IdentityHmacSecretException(
                $"{ConfigurationKey} is required: it keys the identity index every sign-in resolves through. " +
                $"Supply at least {MinimumSecretBytes} cryptographically random bytes, base64 or hex encoded — " +
                $"as {ConfigurationKey} in configuration, or as the environment variable " +
                $"{EnvironmentVariableName}. Under Aspire the hosting integration supplies one automatically; " +
                "elsewhere, generate it once with a cryptographic source (openssl rand -base64 32) and keep it " +
                "in a secret store. CloudLogin never generates a replacement, because a value that differed " +
                "between two starts would fail to resolve any account written by a previous start.");

        byte[] key = DecodeAndValidate(configuredSecret.Trim(), ConfigurationKey);
        List<byte[]> fallbackKeys = [];
        int fallbackIndex = 0;

        foreach (string fallbackSecret in configuredFallbackSecrets ?? [])
        {
            string setting = $"{FallbackConfigurationKey}[{fallbackIndex++}]";

            if (string.IsNullOrWhiteSpace(fallbackSecret))
                throw new IdentityHmacSecretException($"{setting} cannot be empty.");

            byte[] fallbackKey = DecodeAndValidate(fallbackSecret.Trim(), setting);

            if (CryptographicOperations.FixedTimeEquals(key, fallbackKey) ||
                fallbackKeys.Any(existing => CryptographicOperations.FixedTimeEquals(existing, fallbackKey)))
                throw new IdentityHmacSecretException(
                    $"{setting} duplicates the primary key or another fallback key.");

            fallbackKeys.Add(fallbackKey);
        }

        return new IdentityKeyHasher(key, [.. fallbackKeys]);
    }

    /// <summary>Builds a hasher from raw key bytes. For tests and for hosts that hold the key already.</summary>
    public static IdentityKeyHasher FromKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length < MinimumSecretBytes)
            throw new IdentityHmacSecretException(
                $"The identity HMAC key is {key.Length} bytes; at least {MinimumSecretBytes} are required.");

        return new IdentityKeyHasher([.. key], []);
    }

    private static byte[] DecodeAndValidate(string secret, string setting)
    {
        byte[] key;

        // Hex first: a hex string is also valid base64 input in some lengths, and hex is the
        // narrower shape, so testing it first keeps the interpretation unambiguous.
        if (secret.Length % 2 == 0 && secret.All(Uri.IsHexDigit))
        {
            key = Convert.FromHexString(secret);
        }
        else
        {
            try
            {
                key = Convert.FromBase64String(secret);
            }
            catch (FormatException)
            {
                // The malformed value is deliberately not quoted back: an error message is a log
                // line waiting to happen, and this one would be carrying the secret.
                throw new IdentityHmacSecretException(
                    $"{setting} is not valid base64 or hex. Generate it with a cryptographic source, " +
                    $"for example: openssl rand -base64 {MinimumSecretBytes}");
            }
        }

        if (key.Length < MinimumSecretBytes)
            throw new IdentityHmacSecretException(
                $"{setting} decodes to {key.Length} bytes; at least {MinimumSecretBytes} are required.");

        // Not a randomness test - nothing can prove randomness from one sample. This rejects the
        // realistic human-improvised failures (all zeroes, repeated words, near-constant bytes).
        if (key.Distinct().Count() < 8)
            throw new IdentityHmacSecretException(
                $"{setting} decodes to a repeating or near-constant value rather than random bytes. " +
                $"Generate it with a cryptographic source, for example: openssl rand -base64 {MinimumSecretBytes}");

        return key;
    }
}
