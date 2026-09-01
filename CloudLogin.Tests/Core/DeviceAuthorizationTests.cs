using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class DeviceAuthorizationTests
{
    private readonly InMemoryLoginRequestRepository _repository = new();
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly DeviceAuthorizationService _service;

    public DeviceAuthorizationTests() =>
        _service = new DeviceAuthorizationService(_repository, _configuration, new AuditLogger(_audit, _configuration));

    private Task<DeviceAuthorizationStart> BeginAsync() =>
        _service.BeginAsync("https://login.example", "tv-app", "Living room TV", "tv");

    [Fact]
    public async Task Begin_ReturnsRfc8628FieldsAndStoresOnlyHashes()
    {
        DeviceAuthorizationStart start = await BeginAsync();

        Assert.Equal(64, start.DeviceCode.Length); // 32 bytes hex: high entropy
        Assert.Contains("-", start.UserCode);
        Assert.Equal("https://login.example/device", start.VerificationUri);
        Assert.StartsWith(start.VerificationUri + "?user_code=", start.VerificationUriComplete);
        Assert.True(start.ExpiresInSeconds > 0);
        Assert.True(start.IntervalSeconds > 0);

        LoginRequestDocument stored = _repository.Documents.Values.Single();
        Assert.Equal(LoginRequestKinds.Device, stored.Kind);
        Assert.DoesNotContain(start.DeviceCode, System.Text.Json.JsonSerializer.Serialize(stored));
        Assert.Equal(IdentityHashing.Hash(start.DeviceCode), stored.Id);
        Assert.NotNull(stored.Ttl);
        Assert.True(stored.Ttl > 0);
    }

    [Fact]
    public async Task Poll_BeforeApproval_IsPending()
    {
        DeviceAuthorizationStart start = await BeginAsync();

        DevicePollResult result = await _service.PollAsync(start.DeviceCode);

        Assert.Equal(DevicePollOutcomes.AuthorizationPending, result.Outcome);
    }

    [Fact]
    public async Task ApproveThenPoll_SucceedsExactlyOnce()
    {
        DeviceAuthorizationStart start = await BeginAsync();
        Guid approver = Guid.NewGuid();

        Assert.True(await _service.ApproveAsync(start.UserCode, approver));

        // First post-approval poll wins...
        AdvancePollClock(start.DeviceCode);
        DevicePollResult approved = await _service.PollAsync(start.DeviceCode);
        Assert.Equal(DevicePollOutcomes.Approved, approved.Outcome);
        Assert.Equal(approver, approved.UserId);
        Assert.Equal("tv", approved.SignInProfile);

        // ...and consumption is single-use: the next poll is denied.
        AdvancePollClock(start.DeviceCode);
        DevicePollResult second = await _service.PollAsync(start.DeviceCode);
        Assert.Equal(DevicePollOutcomes.AccessDenied, second.Outcome);
    }

    [Fact]
    public async Task Deny_EndsTheRequest()
    {
        DeviceAuthorizationStart start = await BeginAsync();

        Assert.True(await _service.DenyAsync(start.UserCode, Guid.NewGuid()));

        AdvancePollClock(start.DeviceCode);
        DevicePollResult result = await _service.PollAsync(start.DeviceCode);
        Assert.Equal(DevicePollOutcomes.AccessDenied, result.Outcome);
    }

    [Fact]
    public async Task Approve_UnknownOrAlreadyDecidedCode_Fails()
    {
        DeviceAuthorizationStart start = await BeginAsync();

        Assert.False(await _service.ApproveAsync("XXXX-XXXX", Guid.NewGuid()));

        Assert.True(await _service.ApproveAsync(start.UserCode, Guid.NewGuid()));
        Assert.False(await _service.ApproveAsync(start.UserCode, Guid.NewGuid()));
    }

    [Fact]
    public async Task Approve_IsUserCodeFormattingInsensitive()
    {
        DeviceAuthorizationStart start = await BeginAsync();
        string sloppy = start.UserCode.Replace("-", " ").ToLowerInvariant();

        Assert.True(await _service.ApproveAsync(sloppy, Guid.NewGuid()));
    }

    [Fact]
    public async Task Poll_FasterThanInterval_GetsSlowDown()
    {
        DeviceAuthorizationStart start = await BeginAsync();

        await _service.PollAsync(start.DeviceCode);
        DevicePollResult fast = await _service.PollAsync(start.DeviceCode); // immediately again

        Assert.Equal(DevicePollOutcomes.SlowDown, fast.Outcome);
    }

    [Fact]
    public async Task Poll_PersistentViolations_DenyTheRequest()
    {
        _configuration.DeviceAuthorization.MaxPollViolations = 3;
        DeviceAuthorizationStart start = await BeginAsync();

        await _service.PollAsync(start.DeviceCode);

        DevicePollResult last = new() { Outcome = DevicePollOutcomes.AuthorizationPending };
        for (int attempt = 0; attempt < 5; attempt++)
            last = await _service.PollAsync(start.DeviceCode);

        Assert.Equal(DevicePollOutcomes.AccessDenied, last.Outcome);
    }

    [Fact]
    public async Task ExpiredRequest_ReportsExpiredToken_AndCannotBeApproved()
    {
        _configuration.DeviceAuthorization.CodeLifetime = TimeSpan.FromMilliseconds(-1);
        DeviceAuthorizationStart start = await BeginAsync();

        DevicePollResult result = await _service.PollAsync(start.DeviceCode);
        Assert.Equal(DevicePollOutcomes.ExpiredToken, result.Outcome);

        Assert.False(await _service.ApproveAsync(start.UserCode, Guid.NewGuid()));
        Assert.Null(await _service.GetPendingByUserCodeAsync(start.UserCode));
    }

    [Fact]
    public async Task GetPending_ShowsClientDescriptionForConfirmation()
    {
        DeviceAuthorizationStart start = await BeginAsync();

        DeviceApprovalView? pending = await _service.GetPendingByUserCodeAsync(start.UserCode);

        Assert.NotNull(pending);
        Assert.Equal("Living room TV", pending!.ClientDescription);
    }

    /// <summary>Backdates LastPolledOn so the next poll is not an interval violation.</summary>
    private void AdvancePollClock(string deviceCode)
    {
        string id = IdentityHashing.Hash(deviceCode);

        if (_repository.Documents.TryGetValue(id, out LoginRequestDocument? stored) && stored.LastPolledOn is not null)
            stored.LastPolledOn = DateTimeOffset.UtcNow.AddSeconds(-(stored.PollIntervalSeconds + 1));
    }
}
