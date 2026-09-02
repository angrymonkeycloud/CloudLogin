using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class SessionServiceTests
{
    private readonly InMemorySessionRepository _repository = new();
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly SessionService _service;

    public SessionServiceTests() =>
        _service = new SessionService(_repository, _configuration, new AuditLogger(_audit, _configuration));

    private const string WindowsChrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    private const string IPhoneSafari = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    /// <summary>
    /// A device is a sign-in session, not a token family. The browser's own family and every
    /// application family minted from that sign-in share a session id and must show as one row -
    /// the list used to show the same laptop once per application it was signed in to.
    /// </summary>
    [Fact]
    public async Task Devices_GroupEveryFamilyOfOneSessionIntoOneDevice()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult laptop = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience, userAgent: WindowsChrome);
        await AddApplicationFamilyAsync(userId, laptop.SessionId, "portal");
        await AddApplicationFamilyAsync(userId, laptop.SessionId, "agency");
        SessionIssueResult phone = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience, userAgent: IPhoneSafari);

        List<SignedInDevice> devices = await _service.GetDevicesAsync(userId, laptop.SessionId);

        Assert.Equal(2, devices.Count);

        SignedInDevice current = Assert.Single(devices, device => device.IsCurrent);
        Assert.Equal(laptop.FamilyId, current.DeviceId);
        Assert.Equal("Chrome on Windows", current.Name);
        Assert.Equal(["agency", "portal"], current.Audiences);
        Assert.True(current.IsActive);

        SignedInDevice other = Assert.Single(devices, device => !device.IsCurrent);
        Assert.Equal(phone.FamilyId, other.DeviceId);
        Assert.Empty(other.Audiences);
    }

    /// <summary>Signing a device out ends the browser and the applications it signed in to together.</summary>
    [Fact]
    public async Task RevokeDevice_SignsEveryFamilyOfTheSessionOut()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult laptop = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience, userAgent: WindowsChrome);
        string portal = await AddApplicationFamilyAsync(userId, laptop.SessionId, "portal");
        SessionIssueResult phone = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience, userAgent: IPhoneSafari);

        Assert.True(await _service.RevokeDeviceAsync(userId, laptop.FamilyId));

        Assert.False(await _service.IsFamilyActiveAsync(laptop.FamilyId));
        Assert.False(await _service.IsFamilyActiveAsync(portal));
        Assert.True(await _service.IsFamilyActiveAsync(phone.FamilyId));

        SignedInDevice revoked = Assert.Single(await _service.GetDevicesAsync(userId), device => device.DeviceId == laptop.FamilyId);
        Assert.False(revoked.IsActive);
        Assert.Equal(SessionRevocationReasons.UserSignedOut, revoked.RevocationReason);
    }

    /// <summary>"Sign out other devices" keeps every family of the caller's session, applications included.</summary>
    [Fact]
    public async Task RevokeOtherDevices_KeepsTheWholeCurrentSession()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult laptop = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience, userAgent: WindowsChrome);
        string portal = await AddApplicationFamilyAsync(userId, laptop.SessionId, "portal");
        SessionIssueResult phone = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience, userAgent: IPhoneSafari);

        int revoked = await _service.RevokeOtherDevicesAsync(userId, laptop.SessionId);

        Assert.Equal(1, revoked);
        Assert.True(await _service.IsFamilyActiveAsync(laptop.FamilyId));
        Assert.True(await _service.IsFamilyActiveAsync(portal));
        Assert.False(await _service.IsFamilyActiveAsync(phone.FamilyId));
    }

    [Fact]
    public async Task RevokeSession_EndsEveryFamilyOfThatSignIn()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult laptop = await _service.IssueFamilyAsync(userId, audience: SessionService.BrowserAudience);
        string portal = await AddApplicationFamilyAsync(userId, laptop.SessionId, "portal");

        await _service.RevokeSessionAsync(laptop.SessionId, SessionRevocationReasons.UserSignedOut);

        Assert.False(await _service.IsFamilyActiveAsync(laptop.FamilyId));
        Assert.False(await _service.IsFamilyActiveAsync(portal));
        Assert.False(await _service.IsFamilyActiveAsync("never-existed"));
    }

    /// <summary>An application family minted from a browser sign-in, the way the token service records one.</summary>
    private async Task<string> AddApplicationFamilyAsync(Guid userId, string sessionId, string audience)
    {
        string familyId = Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        SessionFamilyDocument family = new()
        {
            Id = familyId,
            FamilyId = familyId,
            UserId = userId.ToString(),
            SessionId = sessionId,
            Audience = audience,
            CurrentTokenId = $"token-{familyId}",
            CreatedOn = now,
            LastSeenOn = now,
            ExpiresOn = now + _configuration.SessionFamilyLifetime
        };

        SessionTokenDocument token = new()
        {
            Id = $"token-{familyId}",
            FamilyId = familyId,
            UserId = userId.ToString(),
            CreatedOn = now,
            ExpiresOn = now + _configuration.RefreshTokenLifetime
        };

        DocumentExpiry.Recompute(family, now);
        DocumentExpiry.Recompute(token, now);
        await _repository.CreateFamilyAsync(family, token);

        return familyId;
    }

    [Fact]
    public async Task IssueFamily_StoresOnlyHashes()
    {
        SessionIssueResult issued = await _service.IssueFamilyAsync(Guid.NewGuid(), audience: "portal");

        Assert.DoesNotContain(_repository.Documents.Values.OfType<SessionTokenDocument>(),
            token => token.Id.Contains(issued.RawRefreshToken, StringComparison.Ordinal));

        // The token document id is the SHA-256 of the raw token, never the raw value.
        SessionTokenDocument token = _repository.Documents.Values.OfType<SessionTokenDocument>().Single();
        Assert.Equal(IdentityHashing.Hash(issued.RawRefreshToken), token.Id);
        Assert.NotNull(token.Ttl);
        Assert.True(token.Ttl > 0);
    }

    [Fact]
    public async Task Rotate_ReturnsNewTokenAndConsumesOld()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult issued = await _service.IssueFamilyAsync(userId);

        SessionIssueResult rotated = await _service.RotateAsync(issued.RawRefreshToken);

        Assert.Equal(userId, rotated.UserId);
        Assert.NotEqual(issued.RawRefreshToken, rotated.RawRefreshToken);
        Assert.Equal(issued.SessionId, rotated.SessionId);

        SessionTokenDocument oldToken = _repository.Documents.Values.OfType<SessionTokenDocument>()
            .Single(token => token.Id == IdentityHashing.Hash(issued.RawRefreshToken));
        Assert.NotNull(oldToken.ConsumedOn);
        Assert.NotNull(oldToken.ReplacedByTokenId);
    }

    [Fact]
    public async Task Rotate_ReusedToken_RevokesWholeFamily()
    {
        SessionIssueResult issued = await _service.IssueFamilyAsync(Guid.NewGuid());
        SessionIssueResult rotated = await _service.RotateAsync(issued.RawRefreshToken);

        // Presenting the consumed first token again is reuse.
        SessionTokenRejectedException rejection = await Assert.ThrowsAsync<SessionTokenRejectedException>(
            () => _service.RotateAsync(issued.RawRefreshToken));
        Assert.True(rejection.FamilyRevoked);

        // The still-newest token is now dead too: the family is burned.
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(rotated.RawRefreshToken));

        SessionFamilyDocument family = _repository.Documents.Values.OfType<SessionFamilyDocument>().Single();
        Assert.True(family.IsRevoked);
        Assert.Equal(SessionRevocationReasons.TokenReuseDetected, family.RevocationReason);

        Assert.Contains(_audit.Events, auditEvent => auditEvent.EventType == "Session.ReuseDetected");
    }

    [Fact]
    public async Task Rotate_NeverExtendsFamilyAbsoluteExpiry()
    {
        SessionIssueResult issued = await _service.IssueFamilyAsync(Guid.NewGuid());
        SessionFamilyDocument before = _repository.Documents.Values.OfType<SessionFamilyDocument>().Single();
        DateTimeOffset absoluteExpiry = before.ExpiresOn!.Value;

        await _service.RotateAsync(issued.RawRefreshToken);

        SessionFamilyDocument after = _repository.Documents.Values.OfType<SessionFamilyDocument>().Single();
        Assert.Equal(absoluteExpiry, after.ExpiresOn);
    }

    [Fact]
    public async Task Rotate_MalformedOrUnknownToken_Rejected()
    {
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync("not-a-token"));
        await Assert.ThrowsAsync<SessionTokenRejectedException>(
            () => _service.RotateAsync($"{Guid.NewGuid():N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    [Fact]
    public async Task Rotate_ConcurrentExchange_HasExactlyOneWinner()
    {
        SessionIssueResult issued = await _service.IssueFamilyAsync(Guid.NewGuid());

        Task<SessionIssueResult>[] attempts =
        [
            Task.Run(() => _service.RotateAsync(issued.RawRefreshToken)),
            Task.Run(() => _service.RotateAsync(issued.RawRefreshToken))
        ];

        int winners = 0, losers = 0;

        foreach (Task<SessionIssueResult> attempt in attempts)
        {
            try { await attempt; winners++; }
            catch (SessionTokenRejectedException) { losers++; }
        }

        Assert.Equal(1, winners);
        Assert.Equal(1, losers);
    }

    [Fact]
    public async Task RevokeAllForUser_KillsEveryFamily()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult first = await _service.IssueFamilyAsync(userId);
        SessionIssueResult second = await _service.IssueFamilyAsync(userId);

        await _service.RevokeAllForUserAsync(userId, SessionRevocationReasons.SecurityStampChanged);

        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(first.RawRefreshToken));
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(second.RawRefreshToken));
    }
}
