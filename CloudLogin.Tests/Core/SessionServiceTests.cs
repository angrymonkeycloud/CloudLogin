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
