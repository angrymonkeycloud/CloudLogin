using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// Security properties of the token authority.
/// <para>
/// These tests exist to pin behaviour that is invisible when it works and
/// catastrophic when it silently stops working: audience isolation, refresh-token
/// reuse detection, and rejection of forged or downgraded signatures.
/// </para>
/// </summary>
public class TokenServiceTests
{
    private const string Authority = "https://login.example.test";
    private const string PortalAudience = "blusky-portal";
    private const string CdmAudience = "cdm-api";

    // ── Test doubles ────────────────────────────────────────────────────────

    private sealed class InMemoryTokenStore : ICloudLoginTokenStore
    {
        private readonly List<CloudLoginSigningKey> _keys = [];
        private readonly List<CloudLoginRefreshToken> _refreshTokens = [];

        public Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudLoginSigningKey>>([.. _keys]);

        public Task SaveSigningKeyAsync(CloudLoginSigningKey key, CancellationToken cancellationToken = default)
        {
            _keys.RemoveAll(existing => existing.KeyId == key.KeyId);
            _keys.Add(key);
            return Task.CompletedTask;
        }

        public Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_refreshTokens.FirstOrDefault(token => token.TokenHash == tokenHash));

        public Task SaveRefreshTokenAsync(CloudLoginRefreshToken token, CancellationToken cancellationToken = default)
        {
            _refreshTokens.RemoveAll(existing => existing.TokenHash == token.TokenHash);
            _refreshTokens.Add(token);
            return Task.CompletedTask;
        }

        public Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default)
        {
            foreach (CloudLoginRefreshToken token in _refreshTokens.Where(token => token.FamilyId == familyId))
                token.IsRevoked = true;

            return Task.CompletedTask;
        }

        public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            foreach (CloudLoginRefreshToken token in _refreshTokens.Where(token => token.SessionId == sessionId))
                token.IsRevoked = true;

            return Task.CompletedTask;
        }

        public Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            foreach (CloudLoginRefreshToken token in _refreshTokens.Where(token => token.UserId == userId))
                token.IsRevoked = true;

            return Task.CompletedTask;
        }
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────

    private static CloudLoginTokenOptions DefaultOptions() => new()
    {
        Issuer = Authority,
        AllowedAudiences = [PortalAudience, CdmAudience],
        AccessTokenLifetime = TimeSpan.FromMinutes(10),
        RefreshTokenLifetime = TimeSpan.FromDays(14),
        SigningKeyPublishGrace = TimeSpan.FromHours(2)
    };

    private static (CloudLoginTokenService Service, CloudLoginSigningKeyManager Keys, InMemoryTokenStore Store)
        CreateService(CloudLoginTokenOptions? options = null)
    {
        options ??= DefaultOptions();
        InMemoryTokenStore store = new();
        IOptions<CloudLoginTokenOptions> wrapped = Options.Create(options);

        CloudLoginSigningKeyManager keys = new(
            store,
            new EphemeralDataProtectionProvider(),
            wrapped,
            NullLogger<CloudLoginSigningKeyManager>.Instance);

        CloudLoginTokenService service = new(
            keys,
            store,
            wrapped,
            NullLogger<CloudLoginTokenService>.Instance);

        return (service, keys, store);
    }

    private static CloudUser CreateUser(Guid? id = null, bool isLocked = false, bool isGlobalAdmin = false) => new()
    {
        ID = id ?? Guid.NewGuid(),
        DisplayName = "Ada Lovelace",
        IsLocked = isLocked,
        IsGlobalAdmin = isGlobalAdmin,
        Inputs =
        [
            new CloudLoginInput { Format = CloudLoginInputFormat.EmailAddress, Input = "ada@example.test", IsPrimary = true }
        ]
    };

    private static Task<CloudUser?> Lookup(CloudUser user, Guid id, CancellationToken _) =>
        Task.FromResult<CloudUser?>(id == user.ID ? user : null);

    // ── Issuance ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssuedTokenCarriesTheSubjectAndValidates()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser(isGlobalAdmin: true);

        CloudLoginTokenResponse response = await service.IssueAsync(user, PortalAudience);

        ClaimsPrincipal? principal = await service.ValidateAccessTokenAsync(response.AccessToken, PortalAudience);

        Assert.NotNull(principal);
        Assert.Equal(user.ID.ToString(), principal.FindFirst(CloudLoginClaims.Subject)?.Value);
        Assert.Equal("True", principal.FindFirst(CloudLoginClaims.IsGlobalAdmin)?.Value, ignoreCase: true);
        Assert.NotNull(response.RefreshToken);
    }

    [Fact]
    public async Task IssuedTokenNeverLeaksThePasswordHash()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();
        user.Inputs[0].Providers.Add(new CloudLoginProvider { Code = "Password", PasswordHash = "super-secret-hash" });

        CloudLoginTokenResponse response = await service.IssueAsync(user, PortalAudience);

        Assert.NotNull(response.User);
        Assert.All(
            response.User.Inputs.SelectMany(input => input.Providers),
            provider => Assert.Null(provider.PasswordHash));
    }

    [Fact]
    public async Task LockedUserCannotBeIssuedAToken()
    {
        (CloudLoginTokenService service, _, _) = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.IssueAsync(CreateUser(isLocked: true), PortalAudience));
    }

    [Fact]
    public async Task UnregisteredAudienceIsRejected()
    {
        (CloudLoginTokenService service, _, _) = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.IssueAsync(CreateUser(), "some-service-we-never-registered"));
    }

    // ── Audience isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task TokenForOneAudienceIsRejectedByAnother()
    {
        // The property that stops a compromised low-value service from replaying a
        // user's token against a high-value one.
        (CloudLoginTokenService service, _, _) = CreateService();

        CloudLoginTokenResponse response = await service.IssueAsync(CreateUser(), PortalAudience);

        Assert.NotNull(await service.ValidateAccessTokenAsync(response.AccessToken, PortalAudience));
        Assert.Null(await service.ValidateAccessTokenAsync(response.AccessToken, CdmAudience));
    }

    // ── Signature integrity ─────────────────────────────────────────────────

    [Fact]
    public async Task TamperedPayloadIsRejected()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();
        Guid victimId = Guid.NewGuid();

        CloudLoginTokenResponse response = await service.IssueAsync(user, PortalAudience);

        string[] parts = response.AccessToken.Split('.');
        string payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        string swapped = payload.Replace(user.ID.ToString(), victimId.ToString());
        string forged = $"{parts[0]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(swapped))}.{parts[2]}";

        Assert.Null(await service.ValidateAccessTokenAsync(forged, PortalAudience));
    }

    [Fact]
    public async Task AlgorithmConfusionTokenIsRejected()
    {
        // The classic attack: re-sign the token with HMAC, using the issuer's *public*
        // key as the shared secret, hoping the verifier accepts whatever "alg" says.
        // Pinning ValidAlgorithms to ES256 is what defeats it.
        (CloudLoginTokenService service, CloudLoginSigningKeyManager keys, _) = CreateService();
        CloudUser user = CreateUser();

        await service.IssueAsync(user, PortalAudience);

        SecurityKey publicKey = (await keys.GetValidationKeysAsync()).First();
        byte[] publicBytes = ((ECDsaSecurityKey)publicKey).ECDsa.ExportSubjectPublicKeyInfo();

        JsonWebTokenHandler handler = new();
        string forged = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = PortalAudience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = new Dictionary<string, object> { [CloudLoginClaims.Subject] = user.ID.ToString() },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(publicBytes) { KeyId = publicKey.KeyId },
                SecurityAlgorithms.HmacSha256)
        });

        Assert.Null(await service.ValidateAccessTokenAsync(forged, PortalAudience));
    }

    [Fact]
    public async Task TokenFromADifferentIssuerIsRejected()
    {
        (CloudLoginTokenService trusted, _, _) = CreateService();

        CloudLoginTokenOptions rogueOptions = DefaultOptions();
        rogueOptions.Issuer = "https://attacker.example.test";
        (CloudLoginTokenService rogue, _, _) = CreateService(rogueOptions);

        CloudLoginTokenResponse forged = await rogue.IssueAsync(CreateUser(), PortalAudience);

        Assert.Null(await trusted.ValidateAccessTokenAsync(forged.AccessToken, PortalAudience));
    }

    [Fact]
    public async Task ExpiredTokenIsRejected()
    {
        CloudLoginTokenOptions options = DefaultOptions();
        options.AccessTokenLifetime = TimeSpan.FromSeconds(1);
        options.ClockSkew = TimeSpan.Zero;
        options.SigningKeyPublishGrace = TimeSpan.FromMinutes(5);

        (CloudLoginTokenService service, _, _) = CreateService(options);

        CloudLoginTokenResponse response = await service.IssueAsync(CreateUser(), PortalAudience);

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.Null(await service.ValidateAccessTokenAsync(response.AccessToken, PortalAudience));
    }

    // ── Refresh rotation and reuse detection ────────────────────────────────

    [Fact]
    public async Task RefreshRotatesTheToken()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();

        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);
        CloudLoginTokenResponse? refreshed = await service.RefreshAsync(
            issued.RefreshToken!,
            (id, token) => Lookup(user, id, token));

        Assert.NotNull(refreshed);
        Assert.NotNull(refreshed.RefreshToken);
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        Assert.NotNull(await service.ValidateAccessTokenAsync(refreshed.AccessToken, PortalAudience));
    }

    [Fact]
    public async Task ConsumedRefreshTokenCannotBeUsedTwice()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();

        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);
        await service.RefreshAsync(issued.RefreshToken!, (id, token) => Lookup(user, id, token));

        CloudLoginTokenResponse? replay = await service.RefreshAsync(
            issued.RefreshToken!,
            (id, token) => Lookup(user, id, token));

        Assert.Null(replay);
    }

    [Fact]
    public async Task ReplayingAConsumedTokenRevokesTheWholeFamily()
    {
        // A consumed token surfacing again means the credential exists in two places.
        // The rotated token the legitimate client holds must die too, otherwise the
        // thief and the victim would simply keep taking turns.
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();

        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);

        CloudLoginTokenResponse? legitimate = await service.RefreshAsync(
            issued.RefreshToken!,
            (id, token) => Lookup(user, id, token));

        Assert.NotNull(legitimate);

        // The attacker replays the stolen original.
        await service.RefreshAsync(issued.RefreshToken!, (id, token) => Lookup(user, id, token));

        // The victim's still-unused token is now dead as well.
        CloudLoginTokenResponse? afterRevocation = await service.RefreshAsync(
            legitimate.RefreshToken!,
            (id, token) => Lookup(user, id, token));

        Assert.Null(afterRevocation);
    }

    [Fact]
    public async Task RefreshIsRefusedOnceTheUserIsLocked()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();

        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);

        CloudUser locked = user with { IsLocked = true };

        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken!,
            (id, token) => Lookup(locked, id, token)));
    }

    [Fact]
    public async Task RevokedSessionCannotRefresh()
    {
        (CloudLoginTokenService service, _, _) = CreateService();
        CloudUser user = CreateUser();

        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);
        await service.RevokeRefreshTokenAsync(issued.RefreshToken!);

        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken!,
            (id, token) => Lookup(user, id, token)));
    }

    [Fact]
    public async Task RefreshTokenIsNotStoredInTheClear()
    {
        (CloudLoginTokenService service, _, InMemoryTokenStore store) = CreateService();
        CloudUser user = CreateUser();

        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);

        // Looking the token up by its own raw value must miss; only the hash is stored.
        Assert.Null(await store.FindRefreshTokenAsync(issued.RefreshToken!));

        string hash = Base64UrlEncoder.Encode(
            SHA256.HashData(Encoding.UTF8.GetBytes(issued.RefreshToken!)));

        Assert.NotNull(await store.FindRefreshTokenAsync(hash));
    }

    // ── Service delegation ──────────────────────────────────────────────────

    [Fact]
    public void PlaintextServiceClientSecret_IsHashedAndClearedDuringValidation()
    {
        ServiceCollection services = new();
        services.AddCloudLoginTokenIssuer(options =>
        {
            options.Issuer = Authority;
            options.AllowedAudiences = [PortalAudience, CdmAudience];
            options.ServiceClients["portal"] = new CloudLoginServiceClient
            {
                ClientSecret = "generated-by-aspire",
                Audience = PortalAudience,
                AllowedAudiences = [CdmAudience]
            };
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        CloudLoginServiceClient client = provider
            .GetRequiredService<IOptions<CloudLoginTokenOptions>>()
            .Value
            .ServiceClients["portal"];

        string expected = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes("generated-by-aspire")));

        Assert.Equal("portal", client.ClientId);
        Assert.Equal(expected, client.SecretHash);
        Assert.Null(client.ClientSecret);
    }

    private static CloudLoginTokenOptions OptionsWithServiceClient(
        string clientId,
        string secret,
        params string[] audiences)
    {
        CloudLoginTokenOptions options = DefaultOptions();

        options.ServiceClients[clientId] = new CloudLoginServiceClient
        {
            ClientId = clientId,
            SecretHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret))),

            // Tokens this client legitimately receives are minted for the portal.
            Audience = PortalAudience,
            AllowedAudiences = [.. audiences]
        };

        return options;
    }

    [Fact]
    public async Task ExchangeKeepsTheUserAsSubjectAndRecordsTheActingService()
    {
        (CloudLoginTokenService service, _, _) = CreateService(
            OptionsWithServiceClient("blusky-portal", "s3cret", CdmAudience));

        CloudUser user = CreateUser();
        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);

        CloudLoginTokenResponse? delegated = await service.ExchangeAsync(
            issued.AccessToken,
            CdmAudience,
            "blusky-portal",
            "s3cret",
            (id, token) => Lookup(user, id, token));

        Assert.NotNull(delegated);

        ClaimsPrincipal? principal = await service.ValidateAccessTokenAsync(delegated.AccessToken, CdmAudience);

        Assert.NotNull(principal);
        Assert.Equal(user.ID.ToString(), principal.FindFirst(CloudLoginClaims.Subject)?.Value);
        Assert.Contains("blusky-portal", principal.FindFirst(CloudLoginClaims.Actor)?.Value ?? string.Empty);

        // Delegation must not hand out a long-lived credential.
        Assert.Null(delegated.RefreshToken);
    }

    [Fact]
    public async Task ExchangeWithAWrongSecretIsRejected()
    {
        (CloudLoginTokenService service, _, _) = CreateService(
            OptionsWithServiceClient("blusky-portal", "s3cret", CdmAudience));

        CloudUser user = CreateUser();
        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);

        Assert.Null(await service.ExchangeAsync(
            issued.AccessToken,
            CdmAudience,
            "blusky-portal",
            "not-the-secret",
            (id, token) => Lookup(user, id, token)));
    }

    [Fact]
    public async Task ExchangeIsRefusedForAnAudienceTheClientMayNotRequest()
    {
        // The portal may call CDM on a user's behalf, but must not be able to mint
        // itself a token for an audience it was never granted.
        (CloudLoginTokenService service, _, _) = CreateService(
            OptionsWithServiceClient("blusky-portal", "s3cret", CdmAudience));

        CloudUser user = CreateUser();
        CloudLoginTokenResponse issued = await service.IssueAsync(user, PortalAudience);

        Assert.Null(await service.ExchangeAsync(
            issued.AccessToken,
            PortalAudience,
            "blusky-portal",
            "s3cret",
            (id, token) => Lookup(user, id, token)));
    }

    [Fact]
    public async Task ExchangeRejectsASubjectTokenMintedForAnotherService()
    {
        // The portal presents a token that was issued to a *different* audience.
        // Accepting it would let any service that got hold of another service's token
        // act on that user's behalf wherever its own grants reach.
        CloudLoginTokenOptions options = OptionsWithServiceClient("blusky-portal", "s3cret", CdmAudience);
        options.ServiceClients["blusky-portal"].Audience = PortalAudience;

        (CloudLoginTokenService service, _, _) = CreateService(options);

        CloudUser user = CreateUser();
        CloudLoginTokenResponse foreignToken = await service.IssueAsync(user, CdmAudience);

        Assert.Null(await service.ExchangeAsync(
            foreignToken.AccessToken,
            CdmAudience,
            "blusky-portal",
            "s3cret",
            (id, token) => Lookup(user, id, token)));
    }

    [Fact]
    public async Task ExchangeRejectsAForgedSubjectToken()
    {
        (CloudLoginTokenService service, _, _) = CreateService(
            OptionsWithServiceClient("blusky-portal", "s3cret", CdmAudience));

        CloudUser user = CreateUser();

        Assert.Null(await service.ExchangeAsync(
            "not.a.token",
            CdmAudience,
            "blusky-portal",
            "s3cret",
            (id, token) => Lookup(user, id, token)));
    }

    // ── Key management ──────────────────────────────────────────────────────

    [Fact]
    public async Task JwksPublishesOnlyPublicKeyMaterial()
    {
        (CloudLoginTokenService service, CloudLoginSigningKeyManager keys, _) = CreateService();

        await service.IssueAsync(CreateUser(), PortalAudience);

        string json = System.Text.Json.JsonSerializer.Serialize(await keys.GetJsonWebKeySetAsync());

        Assert.Contains("\"kty\":\"EC\"", json);
        Assert.Contains("\"alg\":\"ES256\"", json);

        // "d" is the private scalar. Its presence would hand out the signing key.
        Assert.DoesNotContain("\"d\"", json);
    }

    [Fact]
    public async Task PrivateKeyIsNotPersistedInTheClear()
    {
        (CloudLoginTokenService service, _, InMemoryTokenStore store) = CreateService();

        await service.IssueAsync(CreateUser(), PortalAudience);

        CloudLoginSigningKey key = (await store.GetSigningKeysAsync()).Single();

        // Data Protection output must not be a bare PKCS#8 blob.
        byte[] stored = Convert.FromBase64String(key.ProtectedPrivateKey);
        ECDsa probe = ECDsa.Create();

        Assert.ThrowsAny<CryptographicException>(() => probe.ImportPkcs8PrivateKey(stored, out _));
    }

    [Fact]
    public async Task RotationKeepsPreviouslyIssuedTokensValid()
    {
        // Rotation must not log everyone out. Retired keys stay published for the
        // grace window precisely so in-flight tokens keep verifying.
        (CloudLoginTokenService service, CloudLoginSigningKeyManager keys, InMemoryTokenStore store) = CreateService();

        CloudLoginTokenResponse before = await service.IssueAsync(CreateUser(), PortalAudience);

        // Age the current key out of its signing window, leaving it inside the
        // publication grace period.
        CloudLoginSigningKey current = (await store.GetSigningKeysAsync()).Single();
        current.SigningExpiresOn = DateTimeOffset.UtcNow.AddSeconds(-1);
        await store.SaveSigningKeyAsync(current);

        await keys.RotateAsync();

        Assert.Equal(2, (await store.GetSigningKeysAsync()).Count);
        Assert.NotNull(await service.ValidateAccessTokenAsync(before.AccessToken, PortalAudience));
    }
}
