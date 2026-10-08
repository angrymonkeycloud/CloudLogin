using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>Secret keys: as many as needed, with or without an expiry, usable by any website with a backend, revocable at once.</summary>
public class SecretKeyTests
{
    private readonly ControlPlaneFixture _plane = new();

    [Fact]
    public async Task AKey_IsShownOnce_AndOnlyItsHashIsStored()
    {
        IssuedSecretKey issued = await _plane.Keys.CreateAsync(_plane.Admin, "Partner websites", null);

        Assert.StartsWith(SecretKeyService.SecretPrefix, issued.Secret);
        Assert.True(issued.Secret.Length >= 40);

        SecretKeyDocument stored = _plane.SecretKeyStore.Documents[issued.Key.Id];
        Assert.DoesNotContain(issued.Secret, System.Text.Json.JsonSerializer.Serialize(stored));
        Assert.StartsWith(stored.Prefix!, issued.Secret);
        Assert.True(issued.Secret.Length > stored.Prefix!.Length + 20);
    }

    [Fact]
    public async Task AnyNumberOfKeys_CanBeCreated_AndEachWorksForAnyWebsite()
    {
        List<IssuedSecretKey> keys = [];

        for (int index = 0; index < 8; index++)
            keys.Add(await _plane.Keys.CreateAsync(_plane.Admin, $"Key {index}", null));

        foreach (IssuedSecretKey key in keys)
        {
            Assert.True((await _plane.AuthenticateAsync("portal", key.Secret)).Succeeded);
            Assert.True((await _plane.AuthenticateAsync("blog", key.Secret)).Succeeded);
        }

        Assert.Equal(9, (await _plane.Keys.ListAsync()).Count);
    }

    [Fact]
    public async Task OneKey_SharedBySeveralWebsites_IdentifiesEachByItsOwnName()
    {
        IssuedSecretKey shared = await _plane.Keys.CreateAsync(_plane.Admin, "Shared", null);

        ClientIdentity portal = (await _plane.AuthenticateAsync("portal", shared.Secret)).Client!;
        ClientIdentity blog = (await _plane.AuthenticateAsync("blog", shared.Secret)).Client!;

        Assert.Equal("portal", portal.ClientId);
        Assert.Equal("blog", blog.ClientId);
        Assert.Equal(shared.Key.Id, portal.SecretKeyId);
        Assert.Equal(shared.Key.Id, blog.SecretKeyId);
    }

    [Fact]
    public async Task AKeyWithAnExpiry_WorksUntilThen_AndOneWithoutNeverExpires()
    {
        IssuedSecretKey expiring = await _plane.Keys.CreateAsync(_plane.Admin, "Short lived", DateTimeOffset.UtcNow.AddDays(1));
        IssuedSecretKey forever = await _plane.Keys.CreateAsync(_plane.Admin, "Forever", null);

        Assert.True((await _plane.AuthenticateAsync("portal", expiring.Secret)).Succeeded);

        _plane.SecretKeyStore.Documents[expiring.Key.Id].ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1);
        _plane.Directory.Invalidate();

        Assert.Equal(ClientAuthenticationFailures.InvalidSecret, (await _plane.AuthenticateAsync("portal", expiring.Secret)).Failure);
        Assert.True((await _plane.AuthenticateAsync("portal", forever.Secret)).Succeeded);
        Assert.Null(_plane.SecretKeyStore.Documents[forever.Key.Id].ExpiresOn);
        Assert.Equal(SecretKeyStatuses.Expired, (await _plane.Keys.ListAsync()).Single(view => view.Document.Id == expiring.Key.Id).Status);
    }

    [Fact]
    public async Task AnExpiryInThePast_IsRefused()
    {
        AdminException refusal = await Assert.ThrowsAsync<AdminException>(() => _plane.Keys.CreateAsync(_plane.Admin, "Already gone", DateTimeOffset.UtcNow.AddMinutes(-5)));

        Assert.Equal(AdminErrorKinds.Invalid, refusal.Kind);
    }

    [Fact]
    public async Task RevokingAKey_StopsEveryWebsiteUsingIt_AndNoOtherKey()
    {
        IssuedSecretKey revoked = await _plane.Keys.CreateAsync(_plane.Admin, "Leaked", null);
        IssuedSecretKey kept = await _plane.Keys.CreateAsync(_plane.Admin, "Kept", null);

        await _plane.Keys.RevokeAsync(_plane.Admin, revoked.Key.Id, "leaked");

        Assert.False((await _plane.AuthenticateAsync("portal", revoked.Secret)).Succeeded);
        Assert.False((await _plane.AuthenticateAsync("blog", revoked.Secret)).Succeeded);
        Assert.True((await _plane.AuthenticateAsync("portal", kept.Secret)).Succeeded);
        Assert.True((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);

        AdminException again = await Assert.ThrowsAsync<AdminException>(() => _plane.Keys.RevokeAsync(_plane.Admin, revoked.Key.Id, null));
        Assert.Equal(AdminErrorKinds.Conflict, again.Kind);
    }

    [Fact]
    public async Task ADeploymentKey_IsListed_EvenBeforeItIsUsed_AndCanBeRevoked()
    {
        SecretKeyView listed = Assert.Single(await _plane.Keys.ListAsync(), view => view.Document.IsDeployment);

        Assert.Equal(SecretKeyStatuses.Active, listed.Status);
        Assert.Null(listed.Document.Prefix);

        await _plane.Keys.RevokeAsync(_plane.Admin, listed.Document.Id, "rotated in the AppHost");

        Assert.Equal(ClientAuthenticationFailures.InvalidSecret, (await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
        Assert.Equal(ClientAuthenticationFailures.InvalidSecret, (await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
    }

    [Fact]
    public async Task ADeploymentKey_GetsARecordOnFirstUse_ShowingWhoUsedIt()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);

        SecretKeyDocument record = Assert.Single(_plane.SecretKeyStore.Documents.Values);
        Assert.True(record.IsDeployment);
        Assert.Equal("portal", record.LastUsedBy);
        Assert.NotNull(record.LastUsedOn);
    }

    [Fact]
    public async Task AKeysLastUse_IsRecorded()
    {
        IssuedSecretKey key = await _plane.Keys.CreateAsync(_plane.Admin, "Tracked", null);

        await _plane.AuthenticateAsync("blog", key.Secret);

        Assert.Equal("blog", _plane.SecretKeyStore.Documents[key.Key.Id].LastUsedBy);
    }

    [Fact]
    public async Task ANewDeploymentKey_WorksAfterARedeployment_AndTheOldOneStopsWhenRemoved()
    {
        const string replacement = "a-new-deployment-key-for-the-tests-9876543210";
        ClientDirectory redeployed = _plane.Restart(options => options.SecretKeys = [replacement]);

        Assert.True((await redeployed.AuthenticateAsync("portal", replacement)).Succeeded);
        Assert.Equal(ClientAuthenticationFailures.InvalidSecret, (await redeployed.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("https://looks.like/an/origin")]
    [InlineData("name:with-colon")]
    public async Task AnInvalidApplicationName_IsRefused_EvenWithAValidKey(string name)
    {
        Assert.False((await _plane.AuthenticateAsync(name, ControlPlaneFixture.DeploymentKey)).Succeeded);
    }

    [Fact]
    public async Task CreatingAndRevokingKeys_IsAudited_WithoutTheSecret()
    {
        IssuedSecretKey key = await _plane.Keys.CreateAsync(_plane.Admin, "Audited", DateTimeOffset.UtcNow.AddDays(30));
        await _plane.Keys.RevokeAsync(_plane.Admin, key.Key.Id, "done");

        Assert.Equal(_plane.Admin.ToString(), Assert.Single(_plane.Events(AuditEventTypes.SecretKeyCreated)).ActorUserId);
        Assert.Equal(_plane.Admin.ToString(), Assert.Single(_plane.Events(AuditEventTypes.SecretKeyRevoked)).ActorUserId);
        Assert.DoesNotContain(key.Secret, System.Text.Json.JsonSerializer.Serialize(_plane.AuditEvents.Events));
    }

    [Fact]
    public async Task ExpiringKeys_AreReported_AndRevokedOnesAreNot()
    {
        IssuedSecretKey soon = await _plane.Keys.CreateAsync(_plane.Admin, "Soon", DateTimeOffset.UtcNow.AddDays(5));
        IssuedSecretKey later = await _plane.Keys.CreateAsync(_plane.Admin, "Later", DateTimeOffset.UtcNow.AddDays(200));
        IssuedSecretKey revoked = await _plane.Keys.CreateAsync(_plane.Admin, "Revoked", DateTimeOffset.UtcNow.AddDays(3));
        await _plane.Keys.RevokeAsync(_plane.Admin, revoked.Key.Id, null);

        SecretKeyView expiring = Assert.Single(await _plane.Keys.GetExpiringAsync(30));

        Assert.Equal(soon.Key.Id, expiring.Document.Id);
        Assert.NotEqual(later.Key.Id, expiring.Document.Id);
    }
}
