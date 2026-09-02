using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Verification;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// Keeps verification challenges in the core <c>LoginRequests</c> container, beside the device
/// authorization requests they behave exactly like.
/// </summary>
/// <remarks>
/// No new container: the one that already holds short-lived, single-winner, TTL-expiring requests
/// is the one this belongs in. Cosmos TTL removes a spent or abandoned challenge without a sweeper,
/// and the ETag-guarded replace is what makes counting an attempt and spending a code atomic across
/// every instance of the application.
/// </remarks>
internal sealed class CoreVerificationStore(ILoginRequestRepository requests) : ICloudLoginVerificationStore
{
    public Task CreateAsync(VerificationChallenge challenge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        return requests.CreateAsync(ToDocument(challenge), cancellationToken);
    }

    public async Task<VerificationChallenge?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        LoginRequestDocument? document = await requests.GetAsync(id, cancellationToken);

        return document is null || document.Kind != LoginRequestKinds.Verification
            ? null
            : FromDocument(document);
    }

    public Task<bool> TryUpdateAsync(VerificationChallenge challenge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        return requests.TryReplaceAsync(ToDocument(challenge), cancellationToken);
    }

    private static LoginRequestDocument ToDocument(VerificationChallenge challenge)
    {
        LoginRequestDocument document = new()
        {
            Id = challenge.Id,
            Kind = LoginRequestKinds.Verification,
            State = ToState(challenge.State),
            CodeHash = challenge.CodeHash,
            Address = challenge.Address,
            VerificationPurpose = challenge.Purpose,
            AttemptCount = challenge.AttemptCount,
            UserId = challenge.UserId,
            CreatedOn = challenge.CreatedOn,
            ExpiresOn = challenge.ExpiresOn,
            ETag = challenge.ConcurrencyToken
        };

        DocumentExpiry.Recompute(document);

        return document;
    }

    private static VerificationChallenge FromDocument(LoginRequestDocument document) => new()
    {
        Id = document.Id,
        CodeHash = document.CodeHash ?? string.Empty,
        Address = document.Address ?? string.Empty,
        Purpose = document.VerificationPurpose ?? CloudLoginVerificationPurposes.SignIn,
        CreatedOn = document.CreatedOn,
        ExpiresOn = document.ExpiresOn ?? document.CreatedOn,
        State = FromState(document.State),
        AttemptCount = document.AttemptCount,
        UserId = document.UserId,
        ConcurrencyToken = document.ETag
    };

    private static LoginRequestStates ToState(VerificationChallengeStates state) => state switch
    {
        VerificationChallengeStates.Verified => LoginRequestStates.Approved,
        VerificationChallengeStates.Consumed => LoginRequestStates.Consumed,
        VerificationChallengeStates.Denied => LoginRequestStates.Denied,
        _ => LoginRequestStates.Pending
    };

    private static VerificationChallengeStates FromState(LoginRequestStates state) => state switch
    {
        LoginRequestStates.Approved => VerificationChallengeStates.Verified,
        LoginRequestStates.Consumed => VerificationChallengeStates.Consumed,
        LoginRequestStates.Denied => VerificationChallengeStates.Denied,
        _ => VerificationChallengeStates.Pending
    };
}
