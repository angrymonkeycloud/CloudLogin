using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>What checking the configured identity key against the stored one concluded.</summary>
public enum IdentityKeyVerdicts
{
    /// <summary>The configured key is the one this realm's rows were written under.</summary>
    Matches,

    /// <summary>Nothing was recorded yet and the realm holds no identities: a first run.</summary>
    Adopted,

    /// <summary>
    /// Nothing was recorded, but identities already exist. The key cannot be checked against them,
    /// so it is recorded as-is and everything from here is verifiable.
    /// </summary>
    AdoptedWithExistingIdentities,

    /// <summary>A configured fallback key matches: a rotation in progress, now re-recorded.</summary>
    RotatedToPrimary,

    /// <summary>The configured key wrote none of this realm's rows.</summary>
    Mismatch
}

/// <summary>
/// Checks that the configured identity HMAC key is the one this realm's identity index was written
/// under, and refuses to serve on a key that is not.
/// </summary>
/// <remarks>
/// <para>
/// The index resolves every sign-in, and its rows are keyed by that secret. Give the service a
/// different one and nothing breaks loudly: the rows are still there, the lookups are legitimate
/// misses, and external sign-in falls through to registering a second account for someone who
/// already has one. The first account keeps its history and becomes unreachable. That has happened
/// in production and took two days and a hand-written index dump to see.
/// </para>
/// <para>
/// What makes it detectable is a keyed hash of a fixed constant, stored once beside the rows. It
/// proves which key wrote them without revealing it and without storing any identity's plaintext.
/// </para>
/// <para>
/// Deliberately conservative: only a positive mismatch fails. An unreachable store, or a realm with
/// no verifier recorded, cannot prove the key is wrong and must not take a login service down on a
/// suspicion.
/// </para>
/// </remarks>
public sealed class IdentityKeyVerification(
    IIdentityKeyStore identityKeys,
    IdentityKeyHasher hasher,
    CloudLoginCoreConfiguration configuration,
    ILogger<IdentityKeyVerification> logger)
{
    /// <summary>
    /// The constant the verifier hashes. Public by design — its secrecy is not what protects
    /// anything, the key is. Versioned so the construction can change without a false mismatch.
    /// </summary>
    public const string VerifierSubject = "cloudlogin:identity-key-verifier:v1";

    public async Task<IdentityKeyVerdicts> VerifyAsync(CancellationToken cancellationToken = default)
    {
        string realm = configuration.RealmId;
        IReadOnlyList<string> candidates = hasher.ComputeCandidateHashes(VerifierSubject);
        string primary = candidates[0];
        string? stored = await identityKeys.GetKeyVerifierAsync(realm, cancellationToken);

        if (stored is null)
        {
            bool hasIdentities = await identityKeys.HasAnyIdentityAsync(realm, cancellationToken);
            await identityKeys.SetKeyVerifierAsync(realm, primary, cancellationToken);

            if (!hasIdentities)
            {
                logger.LogInformation("CloudLogin recorded the identity key verifier for realm '{Realm}'.", realm);
                return IdentityKeyVerdicts.Adopted;
            }

            // Adoption is the only option: a row's key cannot be checked without the canonical value
            // behind it, which is exactly what the index does not store.
            logger.LogWarning(
                "CloudLogin adopted the configured identity key for realm '{Realm}', which already holds identities. " +
                "Accounts written under a different key cannot be detected retrospectively and would resolve to nothing. " +
                "From now on a changed key is refused at startup.",
                realm);

            return IdentityKeyVerdicts.AdoptedWithExistingIdentities;
        }

        if (string.Equals(stored, primary, StringComparison.Ordinal))
            return IdentityKeyVerdicts.Matches;

        if (candidates.Skip(1).Any(candidate => string.Equals(stored, candidate, StringComparison.Ordinal)))
        {
            await identityKeys.SetKeyVerifierAsync(realm, primary, cancellationToken);
            logger.LogInformation(
                "CloudLogin's identity key rotated for realm '{Realm}': the stored verifier matched a configured " +
                "fallback key and now records the primary one.",
                realm);

            return IdentityKeyVerdicts.RotatedToPrimary;
        }

        return IdentityKeyVerdicts.Mismatch;
    }

    /// <summary>
    /// Runs the check and throws on a mismatch. The message names the setting and the remedy,
    /// because the alternative outcome is duplicate accounts nobody notices.
    /// </summary>
    public async Task VerifyOrThrowAsync(CancellationToken cancellationToken = default)
    {
        if (await VerifyAsync(cancellationToken) is not IdentityKeyVerdicts.Mismatch)
            return;

        throw new IdentityHmacSecretException(
            $"{IdentityKeyHasher.ConfigurationKey} is not the key realm '{configuration.RealmId}' was written under. " +
            "Every existing account would fail to resolve and signing in would create duplicates, so CloudLogin " +
            "stopped instead. Restore the previous value, or - if this is a deliberate rotation - keep it and add the " +
            $"previous one to {IdentityKeyHasher.FallbackConfigurationKey} so existing accounts keep resolving while " +
            "storage converges.");
    }
}
