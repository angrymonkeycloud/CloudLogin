using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server.Verification;

namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Server-issued, server-verified one-time codes.
/// </summary>
/// <remarks>
/// The browser never learns the code and never decides whether it matched: it asks for a challenge,
/// the person types what arrived in their inbox, and the server compares it against a hash it holds
/// and signs them in itself. That is the whole difference from the flow this replaces, where the
/// code was generated in browser code, compared in browser code, and the sign-in that followed was
/// a request the browser made for whichever account it liked.
/// </remarks>
public partial class CloudLoginServer
{
    /// <summary>The authentication type recorded for a sign-in completed by verification code.</summary>
    public const string CodeAuthenticationType = "Code";

    /// <summary>Issues a code for an address and delivers it.</summary>
    public async Task<CloudLoginVerificationChallenge> SendVerificationCode(CloudLoginSendCodeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Address);

        ICloudLoginVerificationStore store = RequireVerificationStore();
        CloudLoginInputFormat format = GetInputFormat(request.Address);

        if (format is not (CloudLoginInputFormat.EmailAddress or CloudLoginInputFormat.PhoneNumber))
            throw new ArgumentException("A verification code needs an email address or a phone number.", nameof(request));

        string address = NormalizeAddress(request.Address, format);
        string handle = CreateHandle();
        string code = CreateCode(_configuration.Security.VerificationCodeLength);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        VerificationChallenge challenge = new()
        {
            Id = HashHandle(handle),
            CodeHash = HashCode(handle, code),
            Address = address,
            Purpose = request.Purpose,
            CreatedOn = now,
            ExpiresOn = now.Add(_configuration.Security.VerificationCodeLifetime)
        };

        await store.CreateAsync(challenge);

        if (format == CloudLoginInputFormat.PhoneNumber)
            await SendWhatsAppCode(address, code);
        else
            await SendEmailCode(address, code);

        return new CloudLoginVerificationChallenge { ChallengeId = handle, ExpiresOn = challenge.ExpiresOn };
    }

    /// <summary>
    /// Redeems a code. A correct code for <see cref="CloudLoginVerificationPurposes.SignIn"/> signs
    /// the caller in before this returns; for every other purpose it hands back the proof the
    /// follow-up call needs.
    /// </summary>
    public async Task<CloudLoginVerificationResult> VerifyCode(CloudLoginVerifyCodeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.Code))
            return CloudLoginVerificationResult.Create(CloudLoginVerificationStatuses.Invalid);

        ICloudLoginVerificationStore store = RequireVerificationStore();
        VerificationChallenge? challenge = await store.GetAsync(HashHandle(request.ChallengeId));

        if (challenge is null || challenge.State != VerificationChallengeStates.Pending)
            return CloudLoginVerificationResult.Create(CloudLoginVerificationStatuses.NotFound);

        if (challenge.HasExpired(DateTimeOffset.UtcNow))
            return CloudLoginVerificationResult.Create(CloudLoginVerificationStatuses.Expired);

        int maximumAttempts = _configuration.Security.MaximumVerificationAttempts;

        if (!FixedTimeEquals(challenge.CodeHash, HashCode(request.ChallengeId, request.Code.Trim())))
        {
            challenge.AttemptCount++;

            bool dead = challenge.AttemptCount >= maximumAttempts;
            challenge.State = dead ? VerificationChallengeStates.Denied : VerificationChallengeStates.Pending;

            // Losing the race means another request counted an attempt against the same challenge:
            // the attempt is recorded either way, so the wrong code still counts.
            await store.TryUpdateAsync(challenge);

            return CloudLoginVerificationResult.Create(
                dead ? CloudLoginVerificationStatuses.TooManyAttempts : CloudLoginVerificationStatuses.Invalid,
                attemptsRemaining: Math.Max(0, maximumAttempts - challenge.AttemptCount));
        }

        CloudUser? user = challenge.Purpose == CloudLoginVerificationPurposes.SignIn
            ? await FindUserByAddress(challenge.Address)
            : null;

        // Verified rather than Consumed when something still has to act on the proven address:
        // creating the account, replacing a password, adding the address to one.
        bool signingIn = user is not null && challenge.Purpose == CloudLoginVerificationPurposes.SignIn;
        challenge.State = signingIn ? VerificationChallengeStates.Consumed : VerificationChallengeStates.Verified;
        challenge.UserId = user?.Id.ToString();

        // The one place a correct code is spent. Whoever loses this race gets nothing, which is
        // what stops the same code being redeemed twice.
        if (!await store.TryUpdateAsync(challenge))
            return CloudLoginVerificationResult.Create(CloudLoginVerificationStatuses.NotFound);

        if (!signingIn)
        {
            return CloudLoginVerificationResult.Create(
                challenge.Purpose == CloudLoginVerificationPurposes.SignIn
                    ? CloudLoginVerificationStatuses.NoAccount
                    : CloudLoginVerificationStatuses.Verified,
                request.ChallengeId);
        }

        user!.LastSignedIn = DateTimeOffset.UtcNow;
        await UpdateUser(user);
        await SignInUserAsync(user, request.KeepMeSignedIn, CodeAuthenticationType);

        return CloudLoginVerificationResult.Create(CloudLoginVerificationStatuses.Verified);
    }

    /// <summary>
    /// Spends a verification token against the address it proved, and fails when it does not prove
    /// exactly that. Every call that acts on a verified address goes through this.
    /// </summary>
    internal async Task ConsumeVerifiedAddress(
        string? verificationToken,
        string address,
        CloudLoginInputFormat format,
        params CloudLoginVerificationPurposes[] acceptedPurposes)
    {
        if (string.IsNullOrWhiteSpace(verificationToken))
            throw new UnauthorizedAccessException("This address has not been verified.");

        ICloudLoginVerificationStore store = RequireVerificationStore();
        VerificationChallenge? challenge = await store.GetAsync(HashHandle(verificationToken));

        if (challenge is null
            || challenge.State != VerificationChallengeStates.Verified
            || !acceptedPurposes.Contains(challenge.Purpose)
            || challenge.HasExpired(DateTimeOffset.UtcNow)
            || !string.Equals(challenge.Address, NormalizeAddress(address, format), StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("This address has not been verified.");
        }

        challenge.State = VerificationChallengeStates.Consumed;

        if (!await store.TryUpdateAsync(challenge))
            throw new UnauthorizedAccessException("This address has not been verified.");
    }

    /// <summary>Signs in the account a consumed registration challenge just created.</summary>
    internal Task SignInVerifiedUser(CloudUser user, bool keepMeSignedIn) =>
        SignInUserAsync(user, keepMeSignedIn, CodeAuthenticationType);

    private async Task<CloudUser?> FindUserByAddress(string address)
    {
        CloudUser? user = GetInputFormat(address) == CloudLoginInputFormat.PhoneNumber
            ? await GetUserByPhoneNumber(address)
            : await GetUserByEmailAddress(address);

        // A test account is reachable only through the explicit test-mode endpoint, exactly as it is
        // for a password sign-in.
        return user is null || user.IsLocked || user.IsTest ? null : user;
    }

    private string NormalizeAddress(string address, CloudLoginInputFormat format) => format switch
    {
        CloudLoginInputFormat.PhoneNumber => GetPhoneNumber(address),
        _ => address.Trim().ToLowerInvariant()
    };

    private ICloudLoginVerificationStore RequireVerificationStore() =>
        _verificationStore ?? throw new InvalidOperationException(
            "Verification codes need an ICloudLoginVerificationStore. It is registered automatically by " +
            "AddCloudLoginWeb; register one explicitly on a host that builds its services by hand.");

    /// <summary>A high-entropy handle for one challenge. Secret, single use, and never stored as it is.</summary>
    private static string CreateHandle() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCode(int length)
    {
        StringBuilder code = new(length);

        for (int digit = 0; digit < length; digit++)
            code.Append(RandomNumberGenerator.GetInt32(10));

        return code.ToString();
    }

    private static string HashHandle(string handle) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handle)));

    /// <summary>
    /// The handle is hashed together with the code, so two challenges holding the same six digits
    /// do not share a hash - and a stolen hash cannot be tried against another challenge.
    /// </summary>
    private static string HashCode(string handle, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{handle}:{code}")));

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
