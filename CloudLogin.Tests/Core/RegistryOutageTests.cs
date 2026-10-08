using AngryMonkey.CloudLogin.Server.Core.Application;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// A revoked key or a blocked application must stay refused when the registry that records it cannot be read. An outage may stop
/// sign-ins; it must never let a revoked key or a blocked application back in.
/// </summary>
public class RegistryOutageTests
{
    private readonly ControlPlaneFixture _plane = new();

    private void Outage(bool down)
    {
        _plane.Applications.Unavailable = down;
        _plane.SecretKeyStore.Unavailable = down;
    }

    [Fact]
    public async Task ARevokedKey_StaysRefused_WarmOrCold()
    {
        IssuedSecretKey key = await _plane.Keys.CreateAsync(_plane.Admin, "Leaked", null);
        await _plane.Keys.RevokeAsync(_plane.Admin, key.Key.Id, null);
        Assert.False((await _plane.AuthenticateAsync("portal", key.Secret)).Succeeded);

        Outage(true);
        _plane.Clock.Advance(TimeSpan.FromSeconds(6));

        Assert.False((await _plane.AuthenticateAsync("portal", key.Secret)).Succeeded);
        Assert.False((await _plane.Restart().AuthenticateAsync("portal", key.Secret)).Succeeded);
    }

    [Fact]
    public async Task ARevokedDeploymentKey_StaysRefused_WarmOrCold()
    {
        string id = Assert.Single(await _plane.Keys.ListAsync()).Document.Id;
        await _plane.Keys.RevokeAsync(_plane.Admin, id, null);

        Outage(true);
        _plane.Clock.Advance(TimeSpan.FromSeconds(6));

        Assert.False((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
        Assert.False((await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
    }

    [Fact]
    public async Task ABlockedApplication_StaysRefused_WarmOrCold()
    {
        await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        await _plane.Apps.BlockAsync(_plane.Admin, "portal", null);

        Outage(true);
        _plane.Clock.Advance(TimeSpan.FromSeconds(6));

        Assert.False((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
        Assert.False((await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
    }

    [Fact]
    public async Task ABlockedPublicSite_StaysRefused_WhileTheRegistryIsUnreadable()
    {
        await _plane.Directory.AuthenticatePublicAsync("https://site.example/callback");
        await _plane.Apps.BlockAsync(_plane.Admin, "https://site.example", null);

        Outage(true);
        _plane.Clock.Advance(TimeSpan.FromSeconds(6));

        Assert.False((await _plane.Directory.AuthenticatePublicAsync("https://site.example/callback")).Succeeded);
        Assert.False((await _plane.Restart().AuthenticatePublicAsync("https://site.example/callback")).Succeeded);
    }

    [Fact]
    public async Task AProcessThatCannotReadTheRegistry_RefusesEvenAnUnrestrictedKey_UnlessTheLegacySwitchIsOn()
    {
        Outage(true);

        ClientAuthenticationResult refused = await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey);
        Assert.Equal(ClientAuthenticationFailures.RegistryUnavailable, refused.Failure);

        Assert.True((await _plane.Restart(options => options.AllowDeploymentKeysWithoutRegistry = true).AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
    }

    [Fact]
    public async Task WhatWasLastRead_KeepsServingWithinTheTolerance_ButNotBeyondIt()
    {
        Assert.True((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);

        Outage(true);
        _plane.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True((await _plane.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);

        Outage(false);
        ClientDirectory impatient = _plane.Restart(options => options.RegistryStaleTolerance = TimeSpan.Zero);
        Assert.True((await impatient.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);

        Outage(true);
        _plane.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(ClientAuthenticationFailures.RegistryUnavailable, (await impatient.AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Failure);
    }

    [Fact]
    public async Task AnAdminKeyCannotBeCheckedWithoutTheRegistry_SoItIsRefused()
    {
        IssuedSecretKey key = await _plane.Keys.CreateAsync(_plane.Admin, "Admin key", null);

        Outage(true);

        Assert.False((await _plane.Restart().AuthenticateAsync("portal", key.Secret)).Succeeded);
    }

    [Fact]
    public async Task WhenTheRegistryReturns_TheStoredStateApplies()
    {
        Outage(true);
        Assert.False((await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);

        Outage(false);
        Assert.True((await _plane.Restart().AuthenticateAsync("portal", ControlPlaneFixture.DeploymentKey)).Succeeded);
    }
}
