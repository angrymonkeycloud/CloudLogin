using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// What CloudLogin does when the secret keying its identity index is not the one that wrote it.
/// </summary>
/// <remarks>
/// This is the failure that produced duplicate accounts in production. Nothing throws on its own:
/// the rows are intact, the lookups are honest misses, and external sign-in falls through to
/// registration. Both halves are pinned here - the startup check that refuses a key which wrote
/// none of this realm, and the registration guard that refuses to fork a person's account when the
/// index and the user store disagree for any other reason.
/// </remarks>
public class IdentityKeyChangeTests
{
    private static IdentityKeyHasher HasherFor(byte[] key) => IdentityKeyHasher.FromKey(key);

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(IdentityKeyHasher.MinimumSecretBytes);

    private static IdentityKeyVerification Verification(IIdentityKeyStore store, IdentityKeyHasher hasher) =>
        new(store, hasher, new CloudLoginCoreConfiguration(), NullLogger<IdentityKeyVerification>.Instance);

    // ── The startup check ─────────────────────────────────────────────────────

    [Fact]
    public async Task AFirstRun_RecordsTheKeyAndPasses()
    {
        IdentityKeyHasher hasher = HasherFor(NewKey());
        InMemoryIdentityKeyStore store = new(hasher);

        Assert.Equal(IdentityKeyVerdicts.Adopted, await Verification(store, hasher).VerifyAsync());
        Assert.Equal(IdentityKeyVerdicts.Matches, await Verification(store, hasher).VerifyAsync());
    }

    [Fact]
    public async Task AChangedKey_IsRefusedInsteadOfSilentlyDuplicating()
    {
        // The production incident in one test: rows written under one key, the service restarted
        // with another. Every lookup would miss and every sign-in would register a new account.
        byte[] original = NewKey();
        IdentityKeyHasher wrote = HasherFor(original);
        InMemoryIdentityKeyStore store = new(wrote);

        await Verification(store, wrote).VerifyAsync();
        await store.InsertAsync("default", new IdentityKeyClaim
        {
            Type = IdentityKeyTypes.Email,
            CanonicalValue = IdentityKey.CanonicalEmail("ada@example.com"),
            UserId = Guid.NewGuid()
        });

        IdentityKeyHasher replaced = HasherFor(NewKey());

        Assert.Equal(IdentityKeyVerdicts.Mismatch, await Verification(store, replaced).VerifyAsync());
        await Assert.ThrowsAsync<IdentityHmacSecretException>(
            () => Verification(store, replaced).VerifyOrThrowAsync());
    }

    [Fact]
    public async Task ADeliberateRotation_IsAcceptedWhenThePreviousKeyIsConfiguredAsAFallback()
    {
        // The supported way to change the key: keep the old one as a read-only fallback so existing
        // accounts keep resolving. The verifier has to follow that, or rotation would be refused.
        byte[] original = NewKey();
        IdentityKeyHasher wrote = HasherFor(original);
        InMemoryIdentityKeyStore store = new(wrote);

        await Verification(store, wrote).VerifyAsync();

        IdentityKeyHasher rotated = IdentityKeyHasher.FromConfiguredSecrets(
            Convert.ToBase64String(NewKey()),
            [Convert.ToBase64String(original)]);

        Assert.Equal(IdentityKeyVerdicts.RotatedToPrimary, await Verification(store, rotated).VerifyAsync());

        // Recorded against the new primary, so dropping the fallback later does not re-trip it.
        Assert.Equal(IdentityKeyVerdicts.Matches, await Verification(store, rotated).VerifyAsync());
    }

    [Fact]
    public async Task AnExistingDeploymentWithNoVerifier_AdoptsItsKeyAndSaysSo()
    {
        // Nothing recorded, but rows exist. A row's key cannot be checked without the canonical
        // value behind it, which the index deliberately does not store - so the only honest move is
        // to adopt and report it, and to be verifiable from then on.
        IdentityKeyHasher hasher = HasherFor(NewKey());
        InMemoryIdentityKeyStore store = new(hasher);

        await store.InsertAsync("default", new IdentityKeyClaim
        {
            Type = IdentityKeyTypes.Email,
            CanonicalValue = IdentityKey.CanonicalEmail("ada@example.com"),
            UserId = Guid.NewGuid()
        });

        Assert.Equal(IdentityKeyVerdicts.AdoptedWithExistingIdentities, await Verification(store, hasher).VerifyAsync());
        Assert.Equal(IdentityKeyVerdicts.Matches, await Verification(store, hasher).VerifyAsync());
    }

    [Fact]
    public async Task TheStoredVerifier_RevealsNothingAboutTheKey()
    {
        IdentityKeyHasher hasher = HasherFor(NewKey());
        InMemoryIdentityKeyStore store = new(hasher);

        await Verification(store, hasher).VerifyAsync();

        string verifier = (await store.GetKeyVerifierAsync("default"))!;

        // A keyed hash of a public constant: the same shape as every other row key, and useless
        // without the key.
        Assert.Equal(64, verifier.Length);
        Assert.Equal(hasher.ComputeHash(IdentityKeyVerification.VerifierSubject), verifier);
    }

    // ── The registration guard ────────────────────────────────────────────────

    [Fact]
    public async Task RegisteringAnEmailAnExistingAccountHolds_IsRefusedWhenTheIndexHasLostIt()
    {
        // The index says the address is free, the user store says an account already has it. That
        // is the state a changed key leaves behind, and registering here is exactly what produced a
        // second Elie Tebchrani and a second Wissam Farhat.
        InMemoryIdentityKeyStore identityKeys = new(TestIdentityHmac.Hasher);
        InMemoryUserRepository users = new();
        InMemoryCredentialRepository credentials = new();
        InMemoryAuditEventRepository audit = new();
        CloudLoginCoreConfiguration configuration = new();
        IdentityLinkingService service = new(
            identityKeys, credentials, users, configuration, new AuditLogger(audit, configuration));

        Guid existingUserId = Guid.NewGuid();
        await users.CreateAsync(new UserDocument
        {
            Id = existingUserId.ToString(),
            Contacts = [new UserContact
            {
                Format = "EmailAddress",
                Value = "ada@example.com",
                NormalizedValue = "ada@example.com",
                IsVerified = true
            }]
        });

        // No identity row for it: the index cannot see what the store plainly holds.
        Assert.Null(await identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")));

        UserDocument arriving = new() { Id = Guid.NewGuid().ToString() };

        IdentityIndexOutOfSyncException exception = await Assert.ThrowsAsync<IdentityIndexOutOfSyncException>(
            () => service.RegisterNewUserAsync(arriving, [new IdentityReservation
            {
                Type = IdentityKeyTypes.Email,
                CanonicalValue = IdentityKey.CanonicalEmail("ada@example.com"),
                IsVerified = true
            }], []));

        Assert.Equal(existingUserId, exception.ExistingUserId);

        // Nothing was written: no second account, and no half-made reservation left behind.
        Assert.Single(users.Documents);
        Assert.Empty(identityKeys.Keys);
    }

    [Fact]
    public async Task AClosedAccountsAddress_CanStillBeRegisteredAgain()
    {
        // A deleted account must not reserve its address forever - that would turn the guard into a
        // way to lock an address out of ever being used again.
        InMemoryIdentityKeyStore identityKeys = new(TestIdentityHmac.Hasher);
        InMemoryUserRepository users = new();
        InMemoryCredentialRepository credentials = new();
        InMemoryAuditEventRepository audit = new();
        CloudLoginCoreConfiguration configuration = new();
        IdentityLinkingService service = new(
            identityKeys, credentials, users, configuration, new AuditLogger(audit, configuration));

        await users.CreateAsync(new UserDocument
        {
            Id = Guid.NewGuid().ToString(),
            State = UserStates.Deleted,
            Contacts = [new UserContact
            {
                Format = "EmailAddress",
                Value = "ada@example.com",
                NormalizedValue = "ada@example.com",
                IsVerified = true
            }]
        });

        UserDocument arriving = new() { Id = Guid.NewGuid().ToString() };

        await service.RegisterNewUserAsync(arriving, [new IdentityReservation
        {
            Type = IdentityKeyTypes.Email,
            CanonicalValue = IdentityKey.CanonicalEmail("ada@example.com"),
            IsVerified = true
        }], []);

        Assert.Equal(2, users.Documents.Count);
    }

    [Fact]
    public async Task AnOrdinaryFirstRegistration_IsUnaffected()
    {
        // The guard costs one index read on a path that already reads the index, and must not
        // change what a normal registration does.
        InMemoryIdentityKeyStore identityKeys = new(TestIdentityHmac.Hasher);
        InMemoryUserRepository users = new();
        InMemoryCredentialRepository credentials = new();
        InMemoryAuditEventRepository audit = new();
        CloudLoginCoreConfiguration configuration = new();
        IdentityLinkingService service = new(
            identityKeys, credentials, users, configuration, new AuditLogger(audit, configuration));

        UserDocument arriving = new() { Id = Guid.NewGuid().ToString() };

        await service.RegisterNewUserAsync(arriving, [new IdentityReservation
        {
            Type = IdentityKeyTypes.Email,
            CanonicalValue = IdentityKey.CanonicalEmail("ada@example.com"),
            IsVerified = true
        }], []);

        Assert.Single(users.Documents);
        Assert.Equal(
            arriving.Id,
            (await identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")))!.UserId.ToString());
    }
}
