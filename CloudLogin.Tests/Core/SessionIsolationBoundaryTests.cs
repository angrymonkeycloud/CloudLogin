using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class SessionIsolationBoundaryTests
{
    private readonly InMemorySessionRepository repository = new();
    private readonly InMemoryAuditEventRepository audit = new();
    private readonly CloudLoginCoreConfiguration configuration = new();
    private SessionService Service => new(repository, configuration, new AuditLogger(audit, configuration));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing-separator")]
    [InlineData(".")]
    [InlineData("family.")]
    [InlineData(".secret")]
    [InlineData("unknown.secret")]
    [InlineData("family.secret.extra")]
    public async Task MalformedRefreshTokensNeverMintSessions(string token)
    {
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => Service.RotateAsync(token));
        Assert.Empty(await Service.GetDevicesAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("portal")]
    [InlineData("api://orders")]
    public async Task RotationPreservesUserAudienceScopeAndFamily(string? audience)
    {
        Guid user = Guid.NewGuid();
        SessionIssueResult original = await Service.IssueFamilyAsync(user, audience, "read write");
        SessionIssueResult rotated = await Service.RotateAsync(original.RawRefreshToken);
        Assert.Equal(user, rotated.UserId);
        Assert.Equal(original.FamilyId, rotated.FamilyId);
        Assert.Equal(original.SessionId, rotated.SessionId);
        Assert.Equal(audience, rotated.Audience);
        Assert.Equal("read write", rotated.Scope);
        Assert.NotEqual(original.RawRefreshToken, rotated.RawRefreshToken);
        Assert.True(await Service.IsFamilyActiveAsync(rotated.FamilyId));
    }

    [Fact]
    public async Task ReusingOldTokenRevokesOnlyItsFamily()
    {
        Guid user = Guid.NewGuid();
        SessionIssueResult original = await Service.IssueFamilyAsync(user);
        SessionIssueResult other = await Service.IssueFamilyAsync(user);
        SessionIssueResult rotated = await Service.RotateAsync(original.RawRefreshToken);
        SessionTokenRejectedException error = await Assert.ThrowsAsync<SessionTokenRejectedException>(() => Service.RotateAsync(original.RawRefreshToken));
        Assert.True(error.FamilyRevoked);
        Assert.False(await Service.IsFamilyActiveAsync(original.FamilyId));
        Assert.True(await Service.IsFamilyActiveAsync(other.FamilyId));
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => Service.RotateAsync(rotated.RawRefreshToken));
    }

    [Fact]
    public async Task DeviceRevocationCannotCrossAccountBoundary()
    {
        Guid owner = Guid.NewGuid();
        SessionIssueResult issued = await Service.IssueFamilyAsync(owner);
        Assert.False(await Service.RevokeDeviceAsync(Guid.NewGuid(), issued.FamilyId));
        Assert.True(await Service.IsFamilyActiveAsync(issued.FamilyId));
        Assert.Empty(await Service.GetDevicesAsync(Guid.NewGuid()));
        Assert.True(await Service.RevokeDeviceAsync(owner, issued.FamilyId));
        Assert.False(await Service.IsFamilyActiveAsync(issued.FamilyId));
    }

    [Fact]
    public async Task RevokeAllDoesNotAffectAnotherUser()
    {
        Guid first = Guid.NewGuid();
        SessionIssueResult firstSession = await Service.IssueFamilyAsync(first);
        SessionIssueResult secondSession = await Service.IssueFamilyAsync(Guid.NewGuid());
        await Service.RevokeAllForUserAsync(first, SessionRevocationReasons.UserSignedOut);
        await Service.RevokeAllForUserAsync(first, SessionRevocationReasons.UserSignedOut);
        Assert.False(await Service.IsFamilyActiveAsync(firstSession.FamilyId));
        Assert.True(await Service.IsFamilyActiveAsync(secondSession.FamilyId));
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(7, 1)]
    public async Task TokenExpiryNeverExceedsFamilyLifetime(int refreshDays, int familyDays)
    {
        configuration.RefreshTokenLifetime = TimeSpan.FromDays(refreshDays);
        configuration.SessionFamilyLifetime = TimeSpan.FromDays(familyDays);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        SessionIssueResult issued = await Service.IssueFamilyAsync(Guid.NewGuid());
        Assert.InRange(issued.ExpiresOn, before.AddDays(Math.Min(refreshDays, familyDays)), DateTimeOffset.UtcNow.AddDays(Math.Min(refreshDays, familyDays)));
    }
}