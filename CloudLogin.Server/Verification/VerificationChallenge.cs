namespace AngryMonkey.CloudLogin.Server.Verification;

/// <summary>Where a verification challenge is in its short life.</summary>
public enum VerificationChallengeStates
{
    /// <summary>Issued, and still accepting the code.</summary>
    Pending,

    /// <summary>The code matched. The follow-up call that acts on the address has yet to arrive.</summary>
    Verified,

    /// <summary>Fully spent: the sign-in, registration or reset it proved is done.</summary>
    Consumed,

    /// <summary>Too many wrong codes. Nothing will be accepted against it again.</summary>
    Denied
}

/// <summary>
/// One verification code, as the server holds it: the code itself is never stored, only a hash it
/// is compared against.
/// </summary>
/// <remarks>
/// The challenge is the whole of the flow's security. It fixes the address, the purpose and the
/// deadline at the moment the code is sent, counts attempts against a code the browser has never
/// seen, and can only ever be spent once - which is what makes an intercepted or guessed code
/// worth nothing on its own.
/// </remarks>
public sealed class VerificationChallenge
{
    /// <summary>
    /// Storage id: the SHA-256 of the handle handed to the browser, so a reader of the database
    /// cannot replay a challenge it did not start.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>SHA-256 of the challenge handle and the code together.</summary>
    public required string CodeHash { get; init; }

    /// <summary>The address the code was delivered to, normalized.</summary>
    public required string Address { get; init; }

    /// <summary>What the code was issued for.</summary>
    public required CloudLoginVerificationPurposes Purpose { get; init; }

    /// <summary>When it was issued.</summary>
    public required DateTimeOffset CreatedOn { get; init; }

    /// <summary>When it stops being accepted.</summary>
    public required DateTimeOffset ExpiresOn { get; init; }

    /// <summary>Where it is in its life.</summary>
    public VerificationChallengeStates State { get; set; } = VerificationChallengeStates.Pending;

    /// <summary>Wrong codes offered so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>The account the challenge resolved to, once it has.</summary>
    public string? UserId { get; set; }

    /// <summary>The store's optimistic-concurrency token, so a challenge has exactly one winner.</summary>
    public string? ConcurrencyToken { get; set; }

    /// <summary>Whether the deadline has passed.</summary>
    public bool HasExpired(DateTimeOffset now) => now >= ExpiresOn;
}

/// <summary>
/// Persistence for verification challenges.
/// </summary>
/// <remarks>
/// <see cref="TryUpdateAsync"/> is the single-winner primitive the whole flow rests on: counting an
/// attempt and spending a challenge both go through it, so two requests racing the same code can
/// never both succeed.
/// </remarks>
public interface ICloudLoginVerificationStore
{
    /// <summary>Stores a newly issued challenge.</summary>
    Task CreateAsync(VerificationChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>Reads a challenge by its storage id, or null when there is none.</summary>
    Task<VerificationChallenge?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Concurrency-guarded update. Returns false rather than throwing when another request changed
    /// the challenge first - losing that race is an expected outcome, not an error.
    /// </summary>
    Task<bool> TryUpdateAsync(VerificationChallenge challenge, CancellationToken cancellationToken = default);
}
