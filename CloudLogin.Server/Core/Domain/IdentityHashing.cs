using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>
/// Deterministic hashing for identity keys and secret lookups. SHA-256, lowercase hex — the
/// same function everywhere so a value hashed at write time can always be found at read time.
/// </summary>
public static class IdentityHashing
{
    /// <summary>SHA-256 of the UTF-8 input, as 64 lowercase hex characters.</summary>
    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// The two-character hash bucket used inside Table Storage partition keys, spreading one
    /// realm's identities over 256 partitions while keeping lookups deterministic.
    /// </summary>
    public static string Bucket(string fullHash) => fullHash[..2];

    /// <summary>Constant-time comparison for hashes of presented secrets.</summary>
    public static bool FixedTimeEquals(string expectedHash, string presentedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHash),
            Encoding.UTF8.GetBytes(presentedHash));
}
