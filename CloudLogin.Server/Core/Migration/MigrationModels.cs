namespace AngryMonkey.CloudLogin.Server.Core.Migration;

public sealed class MigrationOptions
{
    /// <summary>Read, validate, and report without writing anything. The default — writes are opt-in.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Users processed between checkpoint saves.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Resume from the stored checkpoint instead of starting over.</summary>
    public bool ResumeFromCheckpoint { get; set; } = true;

    /// <summary>Also migrate per-user blob security documents (passkeys, authenticator app).</summary>
    public bool IncludeBlobCredentials { get; set; } = true;
}

/// <summary>Restart state. Processing is idempotent, so replaying a batch after a crash is safe.</summary>
public sealed class MigrationCheckpoint
{
    public DateTimeOffset StartedOn { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }
    public HashSet<Guid> ProcessedUserIds { get; set; } = [];

    /// <summary>
    /// Source fingerprint per user. A catch-up pass skips only unchanged documents; an updated
    /// legacy user is replayed idempotently even when its id appeared in an older checkpoint.
    /// </summary>
    public Dictionary<Guid, string> ProcessedFingerprints { get; set; } = [];
}

public enum MigrationIssueSeverities
{
    Info,
    Review,
    Error
}

public sealed record MigrationIssue
{
    public required string Category { get; init; }
    public required MigrationIssueSeverities Severity { get; init; }
    public required string Message { get; init; }
    public Guid? UserId { get; init; }
    public Guid? ConflictingUserId { get; init; }
}

/// <summary>
/// The migration's outcome: counts, validation, and the review list. Duplicate or malformed
/// identities are never resolved by picking a first match — they land here for a person.
/// </summary>
public sealed class MigrationReport
{
    public bool DryRun { get; set; }
    public DateTimeOffset StartedOn { get; set; }
    public DateTimeOffset CompletedOn { get; set; }

    public int UsersRead { get; set; }
    public int UsersMigrated { get; set; }
    public int UsersSkippedAlreadyProcessed { get; set; }
    public int UsersMalformed { get; set; }

    public int IdentityKeysCreated { get; set; }
    public int DuplicateIdentities { get; set; }

    public int PasswordCredentialsCreated { get; set; }
    public int PasskeysMigrated { get; set; }
    public int AuthenticatorsMigrated { get; set; }

    public int ProviderIdentifiersConverted { get; set; }
    public int ProviderIdentifiersFlagged { get; set; }

    public List<MigrationIssue> Issues { get; set; } = [];

    /// <summary>Post-run consistency check results (legacy count vs core count).</summary>
    public int LegacyUserCount { get; set; }
    public int CoreUserCount { get; set; }
    public bool CountsConsistent => LegacyUserCount == CoreUserCount + UsersMalformed;
}

/// <summary>Enumerates the legacy store. Abstracted so tests can feed users without Cosmos.</summary>
public interface ILegacyUserSource
{
    IAsyncEnumerable<CloudUser> EnumerateUsersAsync(CancellationToken cancellationToken = default);
    Task<int> CountUsersAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persists checkpoints and reports. Blob-backed in production, in-memory in tests.</summary>
public interface IMigrationCheckpointStore
{
    Task<MigrationCheckpoint?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MigrationCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task SaveReportAsync(MigrationReport report, CancellationToken cancellationToken = default);
}
