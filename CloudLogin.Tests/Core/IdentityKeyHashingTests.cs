using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using System.Security.Cryptography;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The keyed identity index: how row keys are derived, what the stored entity may and may not
/// contain, and what the configured secret has to be before the application will start.
/// </summary>
public class IdentityKeyHashingTests
{
    private static string RandomSecret(int bytes = IdentityKeyHasher.MinimumSecretBytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    // ── The keyed hash ────────────────────────────────────────────────────────

    [Fact]
    public void Hash_IsHmacNotAPlainDigest()
    {
        IdentityKeyHasher hasher = IdentityKeyHasher.FromConfiguredSecret(RandomSecret());
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");

        // A bare SHA-256 of the canonical string is computable by anyone holding the table; the
        // whole point of keying is that the stored value is not that.
        string plainDigest = IdentityHashing.Hash(canonical);

        Assert.NotEqual(plainDigest, hasher.ComputeHash(canonical));
    }

    [Fact]
    public void Hash_IsStableForTheSameSecretAndValue()
    {
        // Resolution is a point read, so the write-time and read-time hash must agree exactly —
        // including across separately constructed hashers holding the same secret.
        string secret = RandomSecret();
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");

        Assert.Equal(
            IdentityKeyHasher.FromConfiguredSecret(secret).ComputeHash(canonical),
            IdentityKeyHasher.FromConfiguredSecret(secret).ComputeHash(canonical));
    }

    [Fact]
    public void Hash_DiffersUnderADifferentSecret()
    {
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");

        Assert.NotEqual(
            IdentityKeyHasher.FromConfiguredSecret(RandomSecret()).ComputeHash(canonical),
            IdentityKeyHasher.FromConfiguredSecret(RandomSecret()).ComputeHash(canonical));
    }

    [Fact]
    public void Hash_SeparatesIdentityTypesThatShareAValue()
    {
        IdentityKeyHasher hasher = IdentityKeyHasher.FromConfiguredSecret(RandomSecret());

        // The canonical prefixes are what stop a phone number and an email that happen to render
        // the same string from colliding in one index.
        Assert.NotEqual(
            hasher.ComputeHash(IdentityKey.CanonicalEmail("15551234567")),
            hasher.ComputeHash(IdentityKey.CanonicalPhone("15551234567")));
    }

    [Fact]
    public void Hash_IsSixtyFourLowercaseHexCharacters()
    {
        string hash = IdentityKeyHasher.FromConfiguredSecret(RandomSecret())
            .ComputeHash(IdentityKey.CanonicalEmail("ada@example.com"));

        Assert.Equal(64, hash.Length);
        Assert.All(hash, character => Assert.True(Uri.IsHexDigit(character) && !char.IsUpper(character)));
    }

    [Fact]
    public void ExternalIdentity_IsKeyedOnIssuerAndSubject_NeverOnEmail()
    {
        IdentityKeyHasher hasher = IdentityKeyHasher.FromConfiguredSecret(RandomSecret());

        // Same person, same provider, email changed: the identity is unchanged.
        string before = hasher.ComputeHash(IdentityKey.CanonicalExternal("https://accounts.google.com", "sub-1"));
        string after = hasher.ComputeHash(IdentityKey.CanonicalExternal("https://accounts.google.com", "sub-1"));
        Assert.Equal(before, after);

        // Same subject at a different provider is a different identity.
        Assert.NotEqual(before,
            hasher.ComputeHash(IdentityKey.CanonicalExternal("https://login.microsoftonline.com/common/v2.0", "sub-1")));
    }

    // ── Table layout ──────────────────────────────────────────────────────────

    [Fact]
    public void PartitionKey_IsIdentityTypeHashVersionAndBucket()
    {
        IdentityKeyHasher hasher = IdentityKeyHasher.FromConfiguredSecret(RandomSecret());
        string hash = hasher.ComputeHash(IdentityKey.CanonicalEmail("ada@example.com"));

        IdentityKey key = new() { Type = IdentityKeyTypes.Email, Hash = hash, UserId = Guid.NewGuid() };

        Assert.Equal($"Email-v1-{hash[..2]}", key.TablePartitionKey);
        Assert.Equal(hash, key.TableRowKey);
    }

    [Fact]
    public void PartitionKey_SpreadsOneIdentityTypeOverTheBucketRange()
    {
        IdentityKeyHasher hasher = IdentityKeyHasher.FromConfiguredSecret(RandomSecret());

        HashSet<string> buckets = [.. Enumerable.Range(0, 400).Select(index =>
            IdentityKeyHasher.Bucket(hasher.ComputeHash(IdentityKey.CanonicalEmail($"user{index}@example.com"))))];

        // 400 addresses over 256 buckets: a single-partition index would show one bucket here.
        Assert.True(buckets.Count > 100, $"Identities landed in only {buckets.Count} buckets.");
    }

    [Fact]
    public void Entity_CarriesTheVersionTripletAndNoPlaintext()
    {
        IdentityKey key = new()
        {
            Type = IdentityKeyTypes.Email,
            Hash = new string('a', 64),
            UserId = Guid.NewGuid(),
            ContactId = Guid.NewGuid()
        };

        Assert.Equal(1, key.SchemaVersion);
        Assert.Equal(1, key.HashVersion);
        Assert.Equal(1, key.NormalizationVersion);

        // The canonical value is deliberately absent: storing it beside its hash would defeat the
        // keyed hash entirely, and nothing reads it back.
        Assert.DoesNotContain(typeof(IdentityKey).GetProperties(),
            property => property.Name.Contains("Canonical", StringComparison.OrdinalIgnoreCase)
                     || property.Name.Contains("Plain", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("email:ada@example.com", IdentityKeyTypes.Email)]
    [InlineData("phone:+15551234567", IdentityKeyTypes.Phone)]
    [InlineData("ext:https://accounts.google.com|sub-1", IdentityKeyTypes.External)]
    public void TypeOf_RecoversTheTypeFromTheCanonicalValue(string canonicalValue, IdentityKeyTypes expected) =>
        Assert.Equal(expected, IdentityKey.TypeOf(canonicalValue));

    // ── Realm isolation moved from the partition key to the table ─────────────

    [Fact]
    public void DefaultRealm_KeepsTheUnsuffixedTableName() =>
        Assert.Equal(CloudLoginCoreContainers.IdentityKeysTable,
            CloudLoginCoreContainers.IdentityKeysTableFor("default"));

    [Fact]
    public void OtherRealms_GetTheirOwnTable()
    {
        // The realm is no longer in the partition key, so it has to isolate somewhere: without a
        // separate table two realms sharing a storage account would resolve each other's
        // addresses to each other's accounts.
        string table = CloudLoginCoreContainers.IdentityKeysTableFor("tenant-b");

        Assert.NotEqual(CloudLoginCoreContainers.IdentityKeysTable, table);
        Assert.All(table, character => Assert.True(char.IsLetterOrDigit(character)));
    }

    [Fact]
    public async Task Realms_DoNotResolveEachOthersIdentities()
    {
        InMemoryIdentityKeyStore store = new(TestIdentityHmac.Hasher);
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");
        Guid firstRealmUser = Guid.NewGuid();

        await store.InsertAsync("default", new IdentityKeyClaim
        {
            Type = IdentityKeyTypes.Email,
            CanonicalValue = canonical,
            UserId = firstRealmUser
        });

        Assert.Equal(firstRealmUser, (await store.ResolveAsync("default", canonical))!.UserId);
        Assert.Null(await store.ResolveAsync("other", canonical));
    }

    // ── The configured secret ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSecret_FailsWithAnActionableMessage(string? secret)
    {
        IdentityHmacSecretException exception =
            Assert.Throws<IdentityHmacSecretException>(() => IdentityKeyHasher.FromConfiguredSecret(secret));

        // Names both spellings, because which one applies depends on where the server runs.
        Assert.Contains(IdentityKeyHasher.ConfigurationKey, exception.Message);
        Assert.Contains(IdentityKeyHasher.EnvironmentVariableName, exception.Message);

        // Never substituted: a generated stand-in would resolve nothing written by a previous start.
        Assert.Contains("never generates", exception.Message);
    }

    [Fact]
    public void ShortSecret_IsRefused()
    {
        string tooShort = Convert.ToBase64String(RandomNumberGenerator.GetBytes(31));

        IdentityHmacSecretException exception =
            Assert.Throws<IdentityHmacSecretException>(() => IdentityKeyHasher.FromConfiguredSecret(tooShort));

        Assert.Contains("31 bytes", exception.Message);
        Assert.Contains("32", exception.Message);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]                        // all zero bytes
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]    // hex zeroes
    public void NonRandomSecret_IsRefused(string secret)
    {
        IdentityHmacSecretException exception =
            Assert.Throws<IdentityHmacSecretException>(() => IdentityKeyHasher.FromConfiguredSecret(secret));

        Assert.Contains("random", exception.Message);
    }

    [Fact]
    public void MalformedSecret_IsRefused() =>
        Assert.Throws<IdentityHmacSecretException>(
            () => IdentityKeyHasher.FromConfiguredSecret("not base64 or hex !!!!!!!!!!!!!!!!!!!!!!!!!"));

    [Fact]
    public void ExactlyThirtyTwoBytes_IsAccepted() =>
        Assert.NotNull(IdentityKeyHasher.FromConfiguredSecret(RandomSecret(IdentityKeyHasher.MinimumSecretBytes)));

    [Fact]
    public void HexAndBase64_AreBothAccepted()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");

        // The same bytes in either encoding have to produce the same index, or a deployment that
        // reformatted its secret would silently orphan every account.
        Assert.Equal(
            IdentityKeyHasher.FromConfiguredSecret(Convert.ToBase64String(key)).ComputeHash(canonical),
            IdentityKeyHasher.FromConfiguredSecret(Convert.ToHexString(key)).ComputeHash(canonical));
    }

    [Fact]
    public void CandidateHashes_PutPrimaryFirstAndPreserveFallbackOrder()
    {
        string primary = RandomSecret();
        string fallbackOne = RandomSecret();
        string fallbackTwo = RandomSecret();
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");
        IdentityKeyHasher hasher =
            IdentityKeyHasher.FromConfiguredSecrets(primary, [fallbackOne, fallbackTwo]);

        Assert.Equal(
        [
            IdentityKeyHasher.FromConfiguredSecret(primary).ComputeHash(canonical),
            IdentityKeyHasher.FromConfiguredSecret(fallbackOne).ComputeHash(canonical),
            IdentityKeyHasher.FromConfiguredSecret(fallbackTwo).ComputeHash(canonical)
        ], hasher.ComputeCandidateHashes(canonical));
    }

    [Fact]
    public void DuplicateFallbackKeyMaterial_IsRefused()
    {
        byte[] key = RandomNumberGenerator.GetBytes(IdentityKeyHasher.MinimumSecretBytes);
        string primary = Convert.ToBase64String(key);
        string sameBytesAsHex = Convert.ToHexString(key);

        IdentityHmacSecretException exception = Assert.Throws<IdentityHmacSecretException>(
            () => IdentityKeyHasher.FromConfiguredSecrets(primary, [sameBytesAsHex]));

        Assert.Contains("duplicates", exception.Message);
        Assert.DoesNotContain(primary, exception.Message);
    }

    [Fact]
    public async Task FallbackLookup_RekeysToPrimaryWithoutLockingOutTheUser()
    {
        string oldSecret = RandomSecret();
        IdentityKeyHasher oldHasher = IdentityKeyHasher.FromConfiguredSecret(oldSecret);
        IdentityKeyHasher rotatedHasher =
            IdentityKeyHasher.FromConfiguredSecrets(RandomSecret(), [oldSecret]);
        InMemoryIdentityKeyStore store = new(rotatedHasher);
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");
        string oldHash = oldHasher.ComputeHash(canonical);
        Guid userId = Guid.NewGuid();
        (string Table, string Partition, string Row) oldLocation = (
            CloudLoginCoreContainers.IdentityKeysTableFor("default"),
            IdentityKey.PartitionKeyFor(IdentityKeyTypes.Email, IdentityKeyHasher.CurrentHashVersion, oldHash),
            oldHash);
        store.Keys[oldLocation] = new IdentityKey
        {
            Type = IdentityKeyTypes.Email,
            Hash = oldHash,
            UserId = userId
        };

        IdentityKey? resolved = await store.ResolveAsync("default", canonical);
        string primaryHash = rotatedHasher.ComputeHash(canonical);
        (string Table, string Partition, string Row) primaryLocation = (
            CloudLoginCoreContainers.IdentityKeysTableFor("default"),
            IdentityKey.PartitionKeyFor(
                IdentityKeyTypes.Email, IdentityKeyHasher.CurrentHashVersion, primaryHash),
            primaryHash);

        Assert.Equal(userId, resolved?.UserId);
        Assert.True(store.Keys.ContainsKey(primaryLocation));
        Assert.False(store.Keys.ContainsKey(oldLocation));
    }

    [Fact]
    public async Task FallbackClaim_BlocksASecondUserFromClaimingTheSameIdentity()
    {
        string oldSecret = RandomSecret();
        IdentityKeyHasher oldHasher = IdentityKeyHasher.FromConfiguredSecret(oldSecret);
        InMemoryIdentityKeyStore store =
            new(IdentityKeyHasher.FromConfiguredSecrets(RandomSecret(), [oldSecret]));
        string canonical = IdentityKey.CanonicalEmail("ada@example.com");
        string oldHash = oldHasher.ComputeHash(canonical);
        store.Keys[(
            CloudLoginCoreContainers.IdentityKeysTableFor("default"),
            IdentityKey.PartitionKeyFor(IdentityKeyTypes.Email, IdentityKeyHasher.CurrentHashVersion, oldHash),
            oldHash)] = new IdentityKey
            {
                Type = IdentityKeyTypes.Email,
                Hash = oldHash,
                UserId = Guid.NewGuid()
            };

        await Assert.ThrowsAsync<CoreConflictException>(() => store.InsertAsync("default", new IdentityKeyClaim
        {
            Type = IdentityKeyTypes.Email,
            CanonicalValue = canonical,
            UserId = Guid.NewGuid()
        }));
    }

    [Fact]
    public void ErrorMessages_NeverEchoTheSecret()
    {
        const string secret = "SuperSecretValueThatMustNotLeakIntoLogs";

        Exception exception = Assert.ThrowsAny<Exception>(() => IdentityKeyHasher.FromConfiguredSecret(secret));

        Assert.DoesNotContain("SuperSecret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheHasher_HasNoLoggerToLeakThrough()
    {
        // The strongest form of "never logs the secret, the hash, or the input": there is nothing
        // to log through. No constructor takes a logger, and no field holds one, so a future edit
        // that wanted to log a hash would have to add the dependency first - a visible change
        // rather than a one-line accident.
        Assert.DoesNotContain(
            typeof(IdentityKeyHasher).GetConstructors(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType.Name.Contains("Logger", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            typeof(IdentityKeyHasher).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static),
            field => field.FieldType.Name.Contains("Logger", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheHasher_NeverExposesItsKey()
    {
        // Nothing reads the key back out - not a property, not a method. A key that could be read
        // is a key that can be logged, serialized into a diagnostic, or returned by an endpoint.
        IdentityKeyHasher hasher = IdentityKeyHasher.FromConfiguredSecret(RandomSecret());

        Assert.DoesNotContain(
            typeof(IdentityKeyHasher).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
            property => property.PropertyType == typeof(byte[]) || property.PropertyType == typeof(string));

        // And the default ToString cannot render it either.
        Assert.DoesNotContain("=", hasher.ToString() ?? string.Empty);
    }

    [Fact]
    public void TheHasher_ExposesFallbackHashesButNoKeyGenerationOrDerivation()
    {
        string[] members = [.. typeof(IdentityKeyHasher)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Select(member => member.Name)];

        Assert.Contains(nameof(IdentityKeyHasher.ComputeCandidateHashes), members);
        Assert.DoesNotContain(members, name =>
            name.Contains("Derive", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Generate", StringComparison.OrdinalIgnoreCase));
    }
}
