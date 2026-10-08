using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Tests.Core;

internal sealed class ControlPlaneTokenStore : ICloudLoginTokenStore
{
    public List<CloudLoginSigningKey> Keys { get; } = [];
    public List<CloudLoginRefreshToken> RefreshTokens { get; } = [];

    public Task<IReadOnlyList<CloudLoginSigningKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudLoginSigningKey>>([.. Keys]);

    public Task SaveSigningKeyAsync(CloudLoginSigningKey key, CancellationToken cancellationToken = default)
    {
        Keys.RemoveAll(existing => existing.KeyId == key.KeyId);
        Keys.Add(key);
        return Task.CompletedTask;
    }

    public Task<CloudLoginRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(RefreshTokens.FirstOrDefault(token => token.TokenHash == tokenHash));

    public Task SaveRefreshTokenAsync(CloudLoginRefreshToken token, CancellationToken cancellationToken = default)
    {
        RefreshTokens.RemoveAll(existing => existing.TokenHash == token.TokenHash);
        RefreshTokens.Add(token);
        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default)
    {
        foreach (CloudLoginRefreshToken token in RefreshTokens.Where(token => token.FamilyId == familyId))
            token.IsRevoked = true;

        return Task.CompletedTask;
    }

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RevokeUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// The token service over secret keys and observed applications: a key holder may name any audience, a blocked application gets
/// nothing, and a refresh token stays with the application it was issued to whichever key that application uses.
/// </summary>
public class ControlPlaneTokenTests
{
    private readonly ControlPlaneFixture _plane = new();
    private readonly ControlPlaneTokenStore _store = new();
    private readonly CloudLoginTokenService _tokens;
    private readonly CloudLoginSigningKeyManager _keys;

    public ControlPlaneTokenTests()
    {
        _plane.Options.AccessTokenLifetime = TimeSpan.FromMinutes(10);
        _plane.Options.RefreshTokenLifetime = TimeSpan.FromDays(14);

        IOptions<CloudLoginTokenOptions> wrapped = Microsoft.Extensions.Options.Options.Create(_plane.Options);
        _keys = new CloudLoginSigningKeyManager(_store, new EphemeralDataProtectionProvider(), wrapped, NullLogger<CloudLoginSigningKeyManager>.Instance);
        _tokens = new CloudLoginTokenService(_keys, _store, wrapped, NullLogger<CloudLoginTokenService>.Instance, _plane.Directory, _plane.Audit);
    }

    private static CloudUser User() => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Ada",
        LastName = "Lovelace",
        Inputs = [new CloudLoginInput { Format = CloudLoginInputFormat.EmailAddress, Input = "ada@example.test", IsPrimary = true }]
    };

    private static Func<Guid, CancellationToken, Task<CloudUser?>> Lookup(CloudUser user) =>
        (id, _) => Task.FromResult<CloudUser?>(id == user.Id ? user : null);

    private async Task<ClientIdentity> SignInAsAsync(string clientId, string secret = ControlPlaneFixture.DeploymentKey) =>
        (await _plane.AuthenticateAsync(clientId, secret)).Client ?? throw new InvalidOperationException("Authentication failed.");

    [Fact]
    public async Task AKeyHolder_CanHaveTokensIssuedForWhateverAudienceItNames_WithNothingRegistered()
    {
        CloudLoginTokenResponse response = await _tokens.IssueAsync(User(), "any-service-at-all", clientId: "portal");

        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.Equal(10 * 60, response.ExpiresIn);
    }

    [Fact]
    public async Task AnEmptyAudience_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _tokens.IssueAsync(User(), " "));
    }

    [Fact]
    public async Task ARefreshToken_WorksForTheApplicationItWasIssuedTo_WithAnyValidKey()
    {
        IssuedSecretKey other = await _plane.Keys.CreateAsync(_plane.Admin, "Second key", null);
        CloudUser user = User();
        CloudLoginTokenResponse issued = await _tokens.IssueAsync(user, "portal", clientId: "portal");

        CloudLoginTokenResponse? first = await _tokens.RefreshAsync(issued.RefreshToken!, Lookup(user), client: await SignInAsAsync("portal"));
        CloudLoginTokenResponse? second = await _tokens.RefreshAsync(first!.RefreshToken!, Lookup(user), client: await SignInAsAsync("portal", other.Secret));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(await _tokens.RefreshAsync(second!.RefreshToken!, Lookup(user), client: await SignInAsAsync("someone-else")));
    }

    [Fact]
    public async Task ABlockedApplication_GetsNoNewTokens_CannotRefresh_AndCannotAuthenticate()
    {
        CloudUser user = User();
        ClientIdentity portal = await SignInAsAsync("portal");
        CloudLoginTokenResponse issued = await _tokens.IssueAsync(user, "portal", clientId: "portal");

        await _plane.Apps.BlockAsync(_plane.Admin, "portal", "compromised");

        Assert.Equal(ClientAuthenticationFailures.Blocked, (await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _tokens.IssueAsync(user, "portal"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _tokens.IssueAsync(user, "other-service", clientId: "portal"));
        Assert.Null(await _tokens.RefreshAsync(issued.RefreshToken!, Lookup(user), client: portal));

        await _plane.Apps.UnblockAsync(_plane.Admin, "portal");
        Assert.NotNull(await _tokens.RefreshAsync(issued.RefreshToken!, Lookup(user), client: await SignInAsAsync("portal")));
    }

    [Fact]
    public async Task IssuingATokenIsAudited_AgainstTheApplication_WithoutTheToken()
    {
        CloudLoginTokenResponse response = await _tokens.IssueAsync(User(), "portal", clientId: "portal");

        AuditEventDocument item = Assert.Single(_plane.Events(AuditEventTypes.TokenIssued));
        Assert.Equal("portal", item.ClientId);
        Assert.DoesNotContain(response.AccessToken, System.Text.Json.JsonSerializer.Serialize(item));
    }

    // ── Exchange ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenExchange_WorksForAnyKeyHolder_ForAnyAudience()
    {
        CloudUser user = User();
        string subject = (await _tokens.IssueAsync(user, "portal", clientId: "portal")).AccessToken;

        CloudLoginTokenResponse? exchanged = await _tokens.ExchangeAsync(subject, "orders-api", "portal", ControlPlaneFixture.DeploymentKey, Lookup(user));

        Assert.NotNull(exchanged);
    }

    [Fact]
    public async Task TokenExchange_NeedsAValidKey_AndATokenIssuedToTheCaller()
    {
        CloudUser user = User();
        IssuedSecretKey key = await _plane.Keys.CreateAsync(_plane.Admin, "Exchange", null);
        string subject = (await _tokens.IssueAsync(user, "portal", clientId: "portal")).AccessToken;

        Assert.Null(await _tokens.ExchangeAsync(subject, "orders-api", "portal", "not-a-key", Lookup(user)));
        Assert.Null(await _tokens.ExchangeAsync(subject, "orders-api", "another-app", ControlPlaneFixture.DeploymentKey, Lookup(user)));

        await _plane.Keys.RevokeAsync(_plane.Admin, key.Key.Id, null);
        Assert.Null(await _tokens.ExchangeAsync(subject, "orders-api", "portal", key.Secret, Lookup(user)));
    }

    [Fact]
    public async Task TokenExchange_ToABlockedApplication_IsRefused()
    {
        CloudUser user = User();
        string subject = (await _tokens.IssueAsync(user, "portal", clientId: "portal")).AccessToken;
        await SignInAsAsync("orders-api");
        await _plane.Apps.BlockAsync(_plane.Admin, "orders-api", null);

        Assert.Null(await _tokens.ExchangeAsync(subject, "orders-api", "portal", ControlPlaneFixture.DeploymentKey, Lookup(user)));
    }

    [Fact]
    public async Task APublicClient_CannotExchange()
    {
        CloudUser user = User();
        ClientIdentity site = (await _plane.Directory.AuthenticatePublicAsync("https://site.example/callback")).Client!;
        string subject = (await _tokens.IssueAsync(user, site.Audience, clientId: site.ClientId)).AccessToken;

        Assert.Null(await _tokens.ExchangeAsync(subject, "orders-api", site, Lookup(user)));
    }

    // ── Key rotation ────────────────────────────────────────────────────────

    [Fact]
    public async Task AdministratorKeyRotation_StartsSigningWithTheNewKey_AndTheOldOneStillVerifies()
    {
        SigningKeyAdminService service = new(_keys, _plane.Audit);
        CloudUser user = User();
        CloudLoginTokenResponse before = await _tokens.IssueAsync(user, "config-audience");
        CloudLoginSigningKey first = Assert.Single(_store.Keys);

        SigningKeyView rotated = await service.RotateAsync(_plane.Admin);

        Assert.NotEqual(first.KeyId, rotated.KeyId);

        SigningKeyOverview overview = await service.GetOverviewAsync();
        Assert.Equal(2, overview.Keys.Count);
        Assert.Equal(SigningKeyStatuses.Active, Assert.Single(overview.Keys, key => key.KeyId == rotated.KeyId).Status);
        Assert.Equal(SigningKeyStatuses.Retired, Assert.Single(overview.Keys, key => key.KeyId == first.KeyId).Status);

        // A token signed before the rotation still validates, and a new one is signed by the new key.
        Assert.NotNull(await _tokens.ValidateAccessTokenAsync(before.AccessToken, "config-audience"));
        CloudLoginTokenResponse after = await _tokens.IssueAsync(user, "config-audience");
        Assert.NotNull(await _tokens.ValidateAccessTokenAsync(after.AccessToken, "config-audience"));
        Assert.Single(_plane.Events(AuditEventTypes.SigningKeyRotated));
    }
}
