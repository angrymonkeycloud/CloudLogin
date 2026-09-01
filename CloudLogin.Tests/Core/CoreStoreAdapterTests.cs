using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The V2 compatibility adapter: the legacy <c>ICloudLoginStore</c> surface must behave exactly
/// as before while persistence happens in the split core model.
/// </summary>
public class CoreStoreAdapterTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryCredentialRepository _credentials = new();
    private readonly InMemoryIdentityKeyStore _identityKeys = new(TestIdentityHmac.Hasher);
    private readonly InMemoryLoginRequestRepository _loginRequests = new();
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly CoreCloudLoginStoreAdapter _adapter;

    /// <summary>Stands in for the signing-in person's browser when a login request is created.</summary>
    private readonly Microsoft.AspNetCore.Http.DefaultHttpContext _httpContext = new();

    public CoreStoreAdapterTests()
    {
        IdentityNormalization normalization = new(new CloudGeographyClient());
        IdentityLinkingService linking = new(_identityKeys, _credentials, _users, _configuration, new AuditLogger(_audit, _configuration));
        CoreUserService userService = new(_users, _credentials, linking, normalization);

        Microsoft.AspNetCore.Http.HttpContextAccessor accessor = new() { HttpContext = _httpContext };

        _adapter = new CoreCloudLoginStoreAdapter(
            userService, _users, _credentials, linking, normalization, _loginRequests, _configuration, accessor);
    }

    private static CloudUser BuildUser(string email = "ada@example.com", string? passwordHash = "PBKDF2$hash", string? googleSubject = null)
    {
        CloudLoginInput input = new()
        {
            Input = email,
            Format = CloudLoginInputFormat.EmailAddress,
            IsPrimary = true
        };

        if (passwordHash is not null)
            input.Providers.Add(new CloudLoginProvider { Code = "Password", PasswordHash = passwordHash });

        if (googleSubject is not null)
            input.Providers.Add(new CloudLoginProvider { Code = "Google", Identifier = googleSubject });

        return new CloudUser
        {
            ID = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            DisplayName = "Ada Lovelace",
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-2),
            Inputs = [input]
        };
    }

    [Fact]
    public async Task Create_SplitsUserProfileAndCredentials()
    {
        CloudUser user = BuildUser(googleSubject: "google-sub-1");

        await _adapter.Create(user);

        // The user document holds no hash and no subject.
        UserDocument stored = _users.Documents.Values.Single();
        string storedJson = System.Text.Json.JsonSerializer.Serialize(stored);
        Assert.DoesNotContain("PBKDF2$hash", storedJson);
        Assert.DoesNotContain("google-sub-1", storedJson);

        // Credentials moved into their own documents.
        Assert.Contains(_credentials.Documents.Values, credential =>
            credential.Kind == CredentialKinds.Password && credential.PasswordHash == "PBKDF2$hash");
        Assert.Contains(_credentials.Documents.Values, credential =>
            credential.Kind == CredentialKinds.ExternalIdentity && credential.Subject == "google-sub-1");

        // The identity index resolves the email and the external identity.
        Assert.NotNull(await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")));
    }

    [Fact]
    public async Task GetUserById_RecomposesHashesForServerSideLogic()
    {
        CloudUser user = BuildUser();
        await _adapter.Create(user);

        CloudUser? loaded = await _adapter.GetUserById(user.ID);

        // Password login logic reads the hash off the composed user, exactly like V2.
        Assert.NotNull(loaded);
        Assert.Equal("PBKDF2$hash", loaded!.Inputs[0].Providers.Single(provider => provider.Code == "Password").PasswordHash);
    }

    [Fact]
    public async Task GetUserByEmailAddress_MatchesCaseInsensitively()
    {
        CloudUser user = BuildUser("Ada@Example.com");
        await _adapter.Create(user);

        CloudUser? found = await _adapter.GetUserByEmailAddress("ADA@EXAMPLE.COM");

        Assert.NotNull(found);
        Assert.Equal(user.ID, found!.ID);
    }

    [Fact]
    public async Task Update_WithTransportStrippedUser_DoesNotDestroyCredentials()
    {
        CloudUser user = BuildUser();
        await _adapter.Create(user);

        // The account page round-trips a stripped user (hash removed by transport security).
        CloudUser stripped = CloudLoginTransportSecurity.ForTransport(await _adapter.GetUserById(user.ID))!;
        stripped.FirstName = "Augusta";
        await _adapter.Update(stripped);

        CloudUser? reloaded = await _adapter.GetUserById(user.ID);
        Assert.Equal("Augusta", reloaded!.FirstName);
        Assert.Equal("PBKDF2$hash", reloaded.Inputs[0].Providers.Single(provider => provider.Code == "Password").PasswordHash);
    }

    [Fact]
    public async Task FirstUser_BecomesGlobalAdmin_SecondDoesNot()
    {
        CloudUser first = BuildUser("first@example.com");
        CloudUser second = BuildUser("second@example.com");

        await _adapter.Create(first);
        await _adapter.Create(second);

        Assert.True((await _adapter.GetUserById(first.ID))!.IsGlobalAdmin);
        Assert.False((await _adapter.GetUserById(second.ID))!.IsGlobalAdmin);
    }

    [Fact]
    public async Task AddInput_ClaimsTheNewIdentity()
    {
        CloudUser user = BuildUser();
        await _adapter.Create(user);

        await _adapter.AddInput(user.ID, new CloudLoginInput
        {
            Input = "second@example.com",
            Format = CloudLoginInputFormat.EmailAddress
        });

        Assert.Equal(user.ID, (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("second@example.com")))!.UserId);
        Assert.Equal(2, (await _adapter.GetUserById(user.ID))!.Inputs.Count);
    }

    [Fact]
    public async Task DeleteUser_ReleasesIdentitiesAndCredentials()
    {
        CloudUser user = BuildUser(googleSubject: "google-sub-2");
        await _adapter.Create(user);

        await _adapter.DeleteUser(user.ID);

        Assert.Null(await _adapter.GetUserById(user.ID));
        Assert.Null(await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")));
        Assert.Null(await _identityKeys.ResolveAsync("default",
            IdentityKey.CanonicalExternal("https://accounts.google.com", "google-sub-2")));
        Assert.Empty(_credentials.Documents);
    }

    [Fact]
    public async Task DuplicateEmailRegistration_IsRefusedNotMerged()
    {
        await _adapter.Create(BuildUser("ada@example.com"));

        await Assert.ThrowsAsync<IdentityAlreadyLinkedException>(
            () => _adapter.Create(BuildUser("ada@example.com")));
    }

    // ── Classic login requests: single-use semantics preserved ────────────────

    [Fact]
    public async Task LoginRequest_IsConsumedExactlyOnce()
    {
        CloudUser user = BuildUser();
        await _adapter.Create(user);

        Guid requestId = Guid.NewGuid();
        await _adapter.CreateRequest(user.ID, requestId);

        CloudUser? first = await _adapter.GetUserByRequestId(requestId);
        CloudUser? second = await _adapter.GetUserByRequestId(requestId);

        Assert.NotNull(first);
        Assert.Equal(user.ID, first!.ID);
        Assert.Null(second);
    }

    [Fact]
    public async Task LoginRequest_RemembersTheSigningInBrowser()
    {
        // The relying party redeems this over a back channel, where only its own server is
        // visible. Without capturing the browser here, every session would be attributed to the
        // application's server address instead of the person's device.
        CloudUser user = BuildUser();
        await _adapter.Create(user);

        _httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        _httpContext.Request.Headers.UserAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Version/17.0 Mobile/15E148 Safari/604.1";

        Guid requestId = Guid.NewGuid();
        await _adapter.CreateRequest(user.ID, requestId);

        CloudLoginRequestOrigin? origin = await _adapter.GetRequestOrigin(requestId);

        Assert.NotNull(origin);
        Assert.Equal("203.0.113.7", origin!.IpAddress);
        Assert.Contains("iPhone", origin.UserAgent);

        // Reading the origin must not consume the single-use request.
        Assert.NotNull(await _adapter.GetUserByRequestId(requestId));
    }

    [Fact]
    public async Task RequestOrigin_IsNullForAnUnknownOrConsumedRequest()
    {
        Assert.Null(await _adapter.GetRequestOrigin(Guid.NewGuid()));
    }

    [Fact]
    public async Task LoginRequest_CarriesTtl_AndExpiredOnesDoNotResolve()
    {
        CloudUser user = BuildUser();
        await _adapter.Create(user);

        _configuration.LoginRequestLifetime = TimeSpan.FromMilliseconds(-1);
        Guid requestId = Guid.NewGuid();
        await _adapter.CreateRequest(user.ID, requestId);

        LoginRequestDocument stored = _loginRequests.Documents.Values.Single();
        Assert.NotNull(stored.Ttl);

        Assert.Null(await _adapter.GetUserByRequestId(requestId));
    }
}
