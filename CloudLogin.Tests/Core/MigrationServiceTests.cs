using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Core.Migration;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class MigrationServiceTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryCredentialRepository _credentials = new();
    private readonly InMemoryIdentityKeyStore _identityKeys = new(TestIdentityHmac.Hasher);
    private readonly InMemoryMigrationCheckpointStore _checkpoints = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();

    private CloudLoginMigrationService BuildService(List<CloudUser> legacyUsers) => new(
        new ListLegacyUserSource(legacyUsers),
        _checkpoints,
        _users,
        _credentials,
        _identityKeys,
        new IdentityNormalization(new CloudGeographyClient()),
        _configuration);

    private static CloudUser LegacyUser(string email, string? passwordHash = "AQAAAA-hash", string? provider = null, string? identifier = null)
    {
        CloudLoginInput input = new()
        {
            Input = email,
            Format = CloudLoginInputFormat.EmailAddress,
            IsPrimary = true
        };

        if (passwordHash is not null)
            input.Providers.Add(new CloudLoginProvider { Code = "Password", PasswordHash = passwordHash });

        if (provider is not null)
            input.Providers.Add(new CloudLoginProvider { Code = provider, Identifier = identifier });

        return new CloudUser
        {
            ID = Guid.NewGuid(),
            FirstName = "Legacy",
            DisplayName = email,
            CreatedOn = DateTimeOffset.UtcNow.AddYears(-1),
            LastSignedIn = DateTimeOffset.UtcNow.AddDays(-3),
            Inputs = [input]
        };
    }

    [Fact]
    public async Task DryRun_ReadsAndCounts_WritesNothing()
    {
        List<CloudUser> legacy = [LegacyUser("a@example.com"), LegacyUser("b@example.com")];

        MigrationReport report = await BuildService(legacy).RunAsync(new MigrationOptions { DryRun = true });

        Assert.True(report.DryRun);
        Assert.Equal(2, report.UsersRead);
        Assert.Equal(2, report.UsersMigrated);
        Assert.Empty(_users.Documents);
        Assert.Empty(_credentials.Documents);
        Assert.Empty(_identityKeys.Keys);
        Assert.Single(_checkpoints.Reports);
    }

    [Fact]
    public async Task Run_MigratesUsersCredentialsAndIdentityKeys_PreservingIdsAndHashes()
    {
        CloudUser legacyUser = LegacyUser("ada@example.com", provider: "Google", identifier: "sub-google-1");

        MigrationReport report = await BuildService([legacyUser]).RunAsync(new MigrationOptions { DryRun = false });

        Assert.Equal(1, report.UsersMigrated);
        Assert.True(report.CountsConsistent);

        // Ids preserved; profile carried over; nothing secret in the user document.
        UserDocument user = _users.Documents.Values.Single();
        Assert.Equal(legacyUser.ID.ToString(), user.Id);
        Assert.Equal(legacyUser.CreatedOn, user.CreatedOn);
        Assert.DoesNotContain("AQAAAA-hash", System.Text.Json.JsonSerializer.Serialize(user));

        // Hashes preserved verbatim in the Credentials container.
        Assert.Contains(_credentials.Documents.Values, credential =>
            credential.Kind == CredentialKinds.Password && credential.PasswordHash == "AQAAAA-hash");

        // Google has a safely known issuer: converted to (issuer, subject).
        Assert.Equal(1, report.ProviderIdentifiersConverted);
        Assert.Contains(_credentials.Documents.Values, credential =>
            credential.Kind == CredentialKinds.ExternalIdentity &&
            credential.Issuer == "https://accounts.google.com" &&
            credential.Subject == "sub-google-1");

        // Identity keys created for the email and the external identity.
        Assert.Equal(legacyUser.ID, (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")))!.UserId);
        Assert.Equal(legacyUser.ID, (await _identityKeys.ResolveAsync("default",
            IdentityKey.CanonicalExternal("https://accounts.google.com", "sub-google-1")))!.UserId);
    }

    [Fact]
    public async Task UnknownProviderIdentifier_IsFlaggedForReview_NeverGuessed()
    {
        CloudUser legacyUser = LegacyUser("ada@example.com", provider: "WhatsApp", identifier: "wa-opaque-id");

        MigrationReport report = await BuildService([legacyUser]).RunAsync(new MigrationOptions { DryRun = false });

        Assert.Equal(0, report.ProviderIdentifiersConverted);
        Assert.Equal(1, report.ProviderIdentifiersFlagged);
        Assert.Contains(report.Issues, issue => issue.Category == "provider-identifier-unconverted");
        Assert.DoesNotContain(_credentials.Documents.Values, credential => credential.Kind == CredentialKinds.ExternalIdentity);
    }

    [Fact]
    public async Task DuplicateIdentity_ProducesReviewIssue_NeverFirstMatchWins()
    {
        CloudUser first = LegacyUser("shared@example.com");
        CloudUser second = LegacyUser("shared@example.com");

        MigrationReport report = await BuildService([first, second]).RunAsync(new MigrationOptions { DryRun = false });

        // The first claim stands; the second user is reported, not silently merged or dropped.
        Assert.Equal(first.ID, (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("shared@example.com")))!.UserId);
        Assert.True(report.DuplicateIdentities >= 1);
        Assert.Contains(report.Issues, issue =>
            issue.Category == "duplicate-identity" && issue.UserId == second.ID && issue.ConflictingUserId == first.ID);

        // Both user documents still migrate so a human can resolve the collision.
        Assert.Equal(2, _users.Documents.Count);
    }

    [Fact]
    public async Task MalformedUsers_AreReportedAndSkipped()
    {
        CloudUser noInputs = new() { ID = Guid.NewGuid(), DisplayName = "empty" };
        CloudUser noId = new() { Inputs = [new CloudLoginInput { Input = "x@example.com", Format = CloudLoginInputFormat.EmailAddress }] };

        MigrationReport report = await BuildService([noInputs, noId]).RunAsync(new MigrationOptions { DryRun = false });

        Assert.Equal(2, report.UsersMalformed);
        Assert.Equal(0, report.UsersMigrated);
        Assert.Equal(2, report.Issues.Count(issue => issue.Category == "malformed-user"));
        Assert.True(report.CountsConsistent);
    }

    [Fact]
    public async Task Restart_SkipsAlreadyProcessedUsers()
    {
        List<CloudUser> legacy = [LegacyUser("a@example.com"), LegacyUser("b@example.com")];
        CloudLoginMigrationService service = BuildService(legacy);

        await service.RunAsync(new MigrationOptions { DryRun = false });
        Assert.NotNull(_checkpoints.Stored);
        Assert.Equal(2, _checkpoints.Stored!.ProcessedUserIds.Count);

        // A second run (the restart / catch-up pass) reprocesses nothing.
        MigrationReport second = await service.RunAsync(new MigrationOptions { DryRun = false });

        Assert.Equal(2, second.UsersSkippedAlreadyProcessed);
        Assert.Equal(0, second.UsersMigrated);
        Assert.Equal(2, _users.Documents.Count);
    }

    [Fact]
    public async Task CatchUp_ReprocessesAChangedLegacyUser()
    {
        CloudUser legacy = LegacyUser("a@example.com");
        CloudLoginMigrationService service = BuildService([legacy]);
        await service.RunAsync(new MigrationOptions { DryRun = false });

        legacy.DisplayName = "Changed after the first pass";
        MigrationReport catchUp = await service.RunAsync(new MigrationOptions { DryRun = false });

        Assert.Equal(1, catchUp.UsersMigrated);
        Assert.Equal("Changed after the first pass", _users.Documents[legacy.ID.ToString()].DisplayName);
    }

    [Fact]
    public async Task DryRun_DetectsDuplicatesWithinTheSourceRun()
    {
        CloudUser first = LegacyUser("shared@example.com");
        CloudUser second = LegacyUser("shared@example.com");

        MigrationReport report = await BuildService([first, second])
            .RunAsync(new MigrationOptions { DryRun = true });

        Assert.Contains(report.Issues, issue =>
            issue.Category == "duplicate-identity" &&
            issue.UserId == second.ID &&
            issue.ConflictingUserId == first.ID);
    }

    [Fact]
    public async Task Rerun_WithoutCheckpoint_IsIdempotent()
    {
        List<CloudUser> legacy = [LegacyUser("a@example.com")];
        CloudLoginMigrationService service = BuildService(legacy);

        await service.RunAsync(new MigrationOptions { DryRun = false });
        MigrationReport rerun = await service.RunAsync(new MigrationOptions { DryRun = false, ResumeFromCheckpoint = false });

        // Same user replayed: upserts land on the same documents, identity claims are same-user
        // no-ops, and nothing duplicates.
        Assert.Equal(1, rerun.UsersMigrated);
        Assert.Single(_users.Documents);
        Assert.Single(_identityKeys.Keys);
    }

    [Fact]
    public async Task LegacySource_IsNeverMutated()
    {
        // Rollback safety: the migration only reads the legacy store. The list source stands in
        // for the read-only legacy container; its content must be byte-identical afterwards.
        CloudUser legacyUser = LegacyUser("ada@example.com", provider: "Google", identifier: "sub-1");
        string before = System.Text.Json.JsonSerializer.Serialize(legacyUser, CloudLoginSerialization.Options);

        await BuildService([legacyUser]).RunAsync(new MigrationOptions { DryRun = false });

        string after = System.Text.Json.JsonSerializer.Serialize(legacyUser, CloudLoginSerialization.Options);
        Assert.Equal(before, after);
    }
}
