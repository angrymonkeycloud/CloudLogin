using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class DeviceDescriptionTests
{
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    private const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
    private const string IPad = "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/604.1";
    private const string AndroidPhone = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36";
    private const string AndroidTablet = "Mozilla/5.0 (Linux; Android 14; SM-X200) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    [Theory]
    [InlineData(Windows, DeviceTypes.Desktop, "Chrome", "Windows")]
    [InlineData(IPhone, DeviceTypes.Mobile, "Safari", "iPhone")]
    [InlineData(IPad, DeviceTypes.Tablet, "Safari", "iPad")]
    [InlineData(AndroidPhone, DeviceTypes.Mobile, "Chrome", "Android")]
    [InlineData(AndroidTablet, DeviceTypes.Tablet, "Chrome", "Android")]
    public void Parse_IdentifiesTypeBrowserAndOperatingSystem(
        string userAgent, DeviceTypes expectedType, string expectedBrowser, string expectedOperatingSystem)
    {
        DeviceDescription description = DeviceDescription.Parse(userAgent);

        Assert.Equal(expectedType, description.Type);
        Assert.Equal(expectedBrowser, description.Browser);
        Assert.Equal(expectedOperatingSystem, description.OperatingSystem);
        Assert.Equal($"{expectedBrowser} on {expectedOperatingSystem}", description.Name);
    }

    [Fact]
    public void Parse_AndroidTabletIsNotMistakenForAPhone()
    {
        // Android tablets are exactly the Android agents without the "Mobile" token, so checking
        // mobile before tablet would miscategorise every one of them.
        Assert.Equal(DeviceTypes.Tablet, DeviceDescription.Parse(AndroidTablet).Type);
        Assert.Equal(DeviceTypes.Mobile, DeviceDescription.Parse(AndroidPhone).Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-user-agent")]
    public void Parse_NeverThrowsAndNeverReturnsNull(string? userAgent)
    {
        DeviceDescription description = DeviceDescription.Parse(userAgent);

        Assert.NotNull(description);
        Assert.False(string.IsNullOrWhiteSpace(description.Name));
    }
}

public class SignedInDeviceTests
{
    private readonly InMemorySessionRepository _repository = new();
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly SessionService _service;

    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Safari/537.36";
    private const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Version/17.0 Mobile/15E148 Safari/604.1";

    public SignedInDeviceTests() =>
        _service = new SessionService(_repository, _configuration, new AuditLogger(_audit, _configuration));

    [Fact]
    public async Task SigningIn_RecordsTheDevice()
    {
        Guid userId = Guid.NewGuid();
        await _service.IssueFamilyAsync(userId, createdByIp: "203.0.113.7", userAgent: Windows);

        SignedInDevice device = Assert.Single(await _service.GetDevicesAsync(userId));

        Assert.Equal("Chrome on Windows", device.Name);
        Assert.Equal(DeviceTypes.Desktop, device.Type);
        Assert.Equal("203.0.113.7", device.SignedInFromIp);
        Assert.True(device.IsActive);
        Assert.NotNull(device.LastSeenOn);
    }

    [Fact]
    public async Task EachSignIn_IsItsOwnDevice()
    {
        Guid userId = Guid.NewGuid();
        await _service.IssueFamilyAsync(userId, createdByIp: "203.0.113.7", userAgent: Windows);
        await _service.IssueFamilyAsync(userId, createdByIp: "198.51.100.4", userAgent: IPhone);

        List<SignedInDevice> devices = await _service.GetDevicesAsync(userId);

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, device => device.Type == DeviceTypes.Desktop);
        Assert.Contains(devices, device => device.Type == DeviceTypes.Mobile);
        Assert.All(devices, device => Assert.True(device.IsActive));
    }

    [Fact]
    public async Task DevicesOfOtherUsers_AreNeverListed()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        await _service.IssueFamilyAsync(userId, userAgent: Windows);
        await _service.IssueFamilyAsync(otherUser, userAgent: IPhone);

        SignedInDevice device = Assert.Single(await _service.GetDevicesAsync(userId));
        Assert.Equal(DeviceTypes.Desktop, device.Type);
    }

    [Fact]
    public async Task Rotation_UpdatesLastSeen()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult issued = await _service.IssueFamilyAsync(userId, createdByIp: "203.0.113.7", userAgent: Windows);

        await _service.RotateAsync(issued.RawRefreshToken, seenFromIp: "198.51.100.9");

        SignedInDevice device = Assert.Single(await _service.GetDevicesAsync(userId));
        Assert.Equal("198.51.100.9", device.LastSeenIp);
        Assert.Equal("203.0.113.7", device.SignedInFromIp); // the original stays as sign-in origin
    }

    [Fact]
    public async Task SigningOutOneDevice_LeavesTheOthersSignedIn()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult desktop = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        SessionIssueResult phone = await _service.IssueFamilyAsync(userId, userAgent: IPhone);

        Assert.True(await _service.RevokeDeviceAsync(userId, desktop.FamilyId));

        List<SignedInDevice> devices = await _service.GetDevicesAsync(userId);

        SignedInDevice signedOut = Assert.Single(devices, device => device.DeviceId == desktop.FamilyId);
        Assert.False(signedOut.IsActive);
        Assert.Equal(SessionRevocationReasons.UserSignedOut, signedOut.RevocationReason);
        Assert.NotNull(signedOut.RevokedOn);

        SignedInDevice stillIn = Assert.Single(devices, device => device.DeviceId == phone.FamilyId);
        Assert.True(stillIn.IsActive);

        // The revoked device's token is genuinely dead; the other still rotates.
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(desktop.RawRefreshToken));
        await _service.RotateAsync(phone.RawRefreshToken);
    }

    [Fact]
    public async Task RevokingAnotherUsersDevice_IsRefused()
    {
        Guid userId = Guid.NewGuid();
        Guid attacker = Guid.NewGuid();
        SessionIssueResult victim = await _service.IssueFamilyAsync(userId, userAgent: Windows);

        // An id guessed from another account must not revoke anything.
        Assert.False(await _service.RevokeDeviceAsync(attacker, victim.FamilyId));

        SignedInDevice device = Assert.Single(await _service.GetDevicesAsync(userId));
        Assert.True(device.IsActive);
    }

    [Fact]
    public async Task RevokingAnUnknownDevice_IsRefusedRatherThanThrowing()
    {
        Assert.False(await _service.RevokeDeviceAsync(Guid.NewGuid(), "no-such-device"));
    }

    [Fact]
    public async Task RevokingTwice_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult issued = await _service.IssueFamilyAsync(userId, userAgent: Windows);

        Assert.True(await _service.RevokeDeviceAsync(userId, issued.FamilyId));
        Assert.True(await _service.RevokeDeviceAsync(userId, issued.FamilyId));
    }

    [Fact]
    public async Task CurrentDevice_IsMarkedFromTheSessionClaim()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult here = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        await _service.IssueFamilyAsync(userId, userAgent: IPhone);

        List<SignedInDevice> devices = await _service.GetDevicesAsync(userId, currentSessionId: here.SessionId);

        Assert.True(Assert.Single(devices, device => device.DeviceId == here.FamilyId).IsCurrent);
        Assert.Single(devices, device => device.IsCurrent);
    }

    [Fact]
    public async Task ReuseDetection_ShowsWhyTheDeviceStopped()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult issued = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        await _service.RotateAsync(issued.RawRefreshToken);

        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(issued.RawRefreshToken));

        SignedInDevice device = Assert.Single(await _service.GetDevicesAsync(userId));
        Assert.False(device.IsActive);
        Assert.Equal(SessionRevocationReasons.TokenReuseDetected, device.RevocationReason);
    }

    [Fact]
    public async Task ActiveDevicesSortAheadOfSignedOutOnes()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult first = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        await _service.IssueFamilyAsync(userId, userAgent: IPhone);
        await _service.RevokeDeviceAsync(userId, first.FamilyId);

        List<SignedInDevice> devices = await _service.GetDevicesAsync(userId);

        Assert.True(devices[0].IsActive);
        Assert.False(devices[^1].IsActive);
    }

    // ── Signing every other device out ────────────────────────────────────────

    [Fact]
    public async Task SigningOutOtherDevices_KeepsTheCurrentOne()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult here = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        SessionIssueResult phone = await _service.IssueFamilyAsync(userId, userAgent: IPhone);
        SessionIssueResult tablet = await _service.IssueFamilyAsync(userId, userAgent: IPhone);

        Assert.Equal(2, await _service.RevokeOtherDevicesAsync(userId, here.SessionId));

        List<SignedInDevice> devices = await _service.GetDevicesAsync(userId, currentSessionId: here.SessionId);

        Assert.True(Assert.Single(devices, device => device.DeviceId == here.FamilyId).IsActive);
        Assert.False(Assert.Single(devices, device => device.DeviceId == phone.FamilyId).IsActive);
        Assert.False(Assert.Single(devices, device => device.DeviceId == tablet.FamilyId).IsActive);

        // The kept session still works; the others are genuinely dead.
        await _service.RotateAsync(here.RawRefreshToken);
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(phone.RawRefreshToken));
        await Assert.ThrowsAsync<SessionTokenRejectedException>(() => _service.RotateAsync(tablet.RawRefreshToken));
    }

    [Fact]
    public async Task SigningOutOtherDevices_NeverTouchesAnotherAccount()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        SessionIssueResult here = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        SessionIssueResult theirs = await _service.IssueFamilyAsync(otherUser, userAgent: IPhone);

        Assert.Equal(0, await _service.RevokeOtherDevicesAsync(userId, here.SessionId));

        Assert.True(Assert.Single(await _service.GetDevicesAsync(otherUser)).IsActive);
        await _service.RotateAsync(theirs.RawRefreshToken);
    }

    [Fact]
    public async Task SigningOutOtherDevices_WithNothingElseSignedIn_ReportsZero()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult here = await _service.IssueFamilyAsync(userId, userAgent: Windows);

        Assert.Equal(0, await _service.RevokeOtherDevicesAsync(userId, here.SessionId));

        Assert.True(Assert.Single(await _service.GetDevicesAsync(userId)).IsActive);
    }

    [Fact]
    public async Task SigningOutOtherDevices_CountsOnlyDevicesItActuallyRevoked()
    {
        Guid userId = Guid.NewGuid();
        SessionIssueResult here = await _service.IssueFamilyAsync(userId, userAgent: Windows);
        SessionIssueResult phone = await _service.IssueFamilyAsync(userId, userAgent: IPhone);
        SessionIssueResult alreadyOut = await _service.IssueFamilyAsync(userId, userAgent: IPhone);

        await _service.RevokeDeviceAsync(userId, alreadyOut.FamilyId);

        // Already-revoked families are not counted again; the number shown to the person is the
        // number of sessions this action ended.
        Assert.Equal(1, await _service.RevokeOtherDevicesAsync(userId, here.SessionId));
        Assert.False(Assert.Single(await _service.GetDevicesAsync(userId), device => device.DeviceId == phone.FamilyId).IsActive);
    }

    [Fact]
    public async Task SigningOutOtherDevices_WithoutASessionToKeep_RevokesEverything()
    {
        // A caller with no token session of its own — the account page's own cookie sign-in —
        // has nothing to preserve, so "sign out other devices" is every device.
        Guid userId = Guid.NewGuid();
        await _service.IssueFamilyAsync(userId, userAgent: Windows);
        await _service.IssueFamilyAsync(userId, userAgent: IPhone);

        Assert.Equal(2, await _service.RevokeOtherDevicesAsync(userId, currentSessionId: null));

        Assert.All(await _service.GetDevicesAsync(userId), device => Assert.False(device.IsActive));
    }
}
