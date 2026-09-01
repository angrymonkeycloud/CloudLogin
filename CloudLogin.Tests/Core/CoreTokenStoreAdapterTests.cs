using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The legacy refresh-token surface (find by hash, consume, revoke by family/session/user)
/// served from the core Sessions container. Exercises the adapter against the in-memory
/// session repository; the signing-key fallback path needs a real Cosmos container and is
/// covered by the provisioning contract instead.
/// </summary>
public class CoreTokenStoreAdapterTests
{
    private readonly InMemorySessionRepository _sessions = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly CoreTokenStoreAdapter _adapter;

    public CoreTokenStoreAdapterTests() =>
        _adapter = new CoreTokenStoreAdapter(_sessions, database: null!, _configuration);

    private static CloudLoginRefreshToken NewToken(Guid userId, string familyId, string sessionId, string hash)
    {
        CloudLoginRefreshToken token = new()
        {
            TokenHash = hash,
            UserId = userId,
            FamilyId = familyId,
            SessionId = sessionId,
            Audience = "portal",
            CreatedOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(14)
        };

        token.SetId(Guid.NewGuid());
        return token;
    }

    [Fact]
    public async Task SaveAndFind_RoundTripsTheLegacyShape()
    {
        Guid userId = Guid.NewGuid();
        CloudLoginRefreshToken token = NewToken(userId, "family-1", "sess-1", "hash-1");

        await _adapter.SaveRefreshTokenAsync(token);

        CloudLoginRefreshToken? found = await _adapter.FindRefreshTokenAsync("hash-1");

        Assert.NotNull(found);
        Assert.Equal(userId, found!.UserId);
        Assert.Equal("family-1", found.FamilyId);
        Assert.Equal("sess-1", found.SessionId);
        Assert.Equal("portal", found.Audience);
        Assert.False(found.IsRevoked);
        Assert.Null(found.ConsumedOn);

        // Underneath: a family head plus a token document with a recomputed positive ttl.
        SessionTokenDocument stored = _sessions.Documents.Values.OfType<SessionTokenDocument>().Single();
        Assert.True(stored.Ttl > 0);
    }

    [Fact]
    public async Task Consume_PersistsConsumedOn_ForReuseDetection()
    {
        CloudLoginRefreshToken token = NewToken(Guid.NewGuid(), "family-2", "sess-2", "hash-2");
        await _adapter.SaveRefreshTokenAsync(token);

        CloudLoginRefreshToken stored = (await _adapter.FindRefreshTokenAsync("hash-2"))!;
        stored.ConsumedOn = DateTimeOffset.UtcNow;
        await _adapter.SaveRefreshTokenAsync(stored);

        CloudLoginRefreshToken reread = (await _adapter.FindRefreshTokenAsync("hash-2"))!;
        Assert.NotNull(reread.ConsumedOn);
        Assert.False(reread.IsActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RevokeFamily_MakesEveryTokenInactive()
    {
        Guid userId = Guid.NewGuid();
        await _adapter.SaveRefreshTokenAsync(NewToken(userId, "family-3", "sess-3", "hash-3a"));
        await _adapter.SaveRefreshTokenAsync(NewToken(userId, "family-3", "sess-3", "hash-3b"));

        await _adapter.RevokeFamilyAsync("family-3");

        Assert.True((await _adapter.FindRefreshTokenAsync("hash-3a"))!.IsRevoked);
        Assert.True((await _adapter.FindRefreshTokenAsync("hash-3b"))!.IsRevoked);
    }

    [Fact]
    public async Task RevokeSession_HitsOnlyThatSessionsFamilies()
    {
        Guid userId = Guid.NewGuid();
        await _adapter.SaveRefreshTokenAsync(NewToken(userId, "family-4", "sess-4", "hash-4"));
        await _adapter.SaveRefreshTokenAsync(NewToken(userId, "family-5", "sess-5", "hash-5"));

        await _adapter.RevokeSessionAsync("sess-4");

        Assert.True((await _adapter.FindRefreshTokenAsync("hash-4"))!.IsRevoked);
        Assert.False((await _adapter.FindRefreshTokenAsync("hash-5"))!.IsRevoked);
    }

    [Fact]
    public async Task RevokeUser_HitsEveryFamilyOfTheUser()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        await _adapter.SaveRefreshTokenAsync(NewToken(userId, "family-6", "sess-6", "hash-6"));
        await _adapter.SaveRefreshTokenAsync(NewToken(userId, "family-7", "sess-7", "hash-7"));
        await _adapter.SaveRefreshTokenAsync(NewToken(otherUser, "family-8", "sess-8", "hash-8"));

        await _adapter.RevokeUserAsync(userId);

        Assert.True((await _adapter.FindRefreshTokenAsync("hash-6"))!.IsRevoked);
        Assert.True((await _adapter.FindRefreshTokenAsync("hash-7"))!.IsRevoked);
        Assert.False((await _adapter.FindRefreshTokenAsync("hash-8"))!.IsRevoked);
    }

    [Fact]
    public async Task UnknownHash_FindsNothing()
    {
        Assert.Null(await _adapter.FindRefreshTokenAsync("no-such-hash"));
    }

    [Fact]
    public async Task ApplicationSignIn_DescribesTheDevice()
    {
        // Applications sign users in through this adapter rather than through
        // SessionService.IssueFamilyAsync, so the device must be described here too — otherwise
        // every real sign-in would list as an unnamed "Unknown device".
        Guid userId = Guid.NewGuid();
        CloudLoginRefreshToken token = NewToken(userId, "family-device", "sess-device", "hash-device");
        token.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Version/17.0 Mobile/15E148 Safari/604.1";
        token.CreatedByIp = "203.0.113.7";

        await _adapter.SaveRefreshTokenAsync(token);

        SessionFamilyDocument family = _sessions.Documents.Values.OfType<SessionFamilyDocument>().Single();

        Assert.Equal("Safari on iPhone", family.DeviceName);
        Assert.Equal(DeviceTypes.Mobile, family.DeviceType);
        Assert.Equal("203.0.113.7", family.LastSeenIp);
        Assert.NotNull(family.LastSeenOn);
    }
}
