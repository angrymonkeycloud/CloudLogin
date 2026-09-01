using System.Security.Cryptography;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>RFC 8628 poll outcomes, mapped 1:1 onto the token endpoint's error codes.</summary>
public enum DevicePollOutcomes
{
    AuthorizationPending,
    SlowDown,
    AccessDenied,
    ExpiredToken,
    Approved
}

public sealed record DeviceAuthorizationStart
{
    public required string DeviceCode { get; init; }
    public required string UserCode { get; init; }
    public required string VerificationUri { get; init; }
    public required string VerificationUriComplete { get; init; }
    public required int ExpiresInSeconds { get; init; }
    public required int IntervalSeconds { get; init; }
}

public sealed record DevicePollResult
{
    public required DevicePollOutcomes Outcome { get; init; }
    public Guid? UserId { get; init; }
    public string? SignInProfile { get; init; }
    public Guid? RequestId { get; init; }
}

public sealed record DeviceApprovalView
{
    public required string UserCode { get; init; }
    public required string ClientDescription { get; init; }
    public required DateTimeOffset ExpiresOn { get; init; }
}

/// <summary>
/// OAuth 2.0 Device Authorization Grant (RFC 8628) over the <c>LoginRequests</c> container —
/// the transport behind QR and TV sign-in. QR is a transport mechanism, not an identity
/// provider: the mobile approval page authenticates the person with the sign-in profile's
/// configured methods; this service only brokers the request between the two devices.
/// <para>
/// Only hashes of <c>device_code</c> and <c>user_code</c> are persisted. The QR code encodes
/// the verification URL alone; the user code is displayed beside it so the person can confirm
/// they are approving the request they are looking at (phishing resistance). Every state
/// transition is an ETag-conditional replace, so approval and consumption each happen exactly
/// once.
/// </para>
/// </summary>
public sealed class DeviceAuthorizationService(
    ILoginRequestRepository repository,
    CloudLoginCoreConfiguration configuration,
    IAuditLogger audit,
    SignInProfileConfiguration? signInProfiles = null)
{
    private readonly ILoginRequestRepository _repository = repository;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly IAuditLogger _audit = audit;
    private readonly SignInProfileConfiguration? _signInProfiles = signInProfiles;

    /// <summary>Characters for user codes: no vowels (no words), no 0/O/1/I lookalikes.</summary>
    private const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ23456789";

    public async Task<DeviceAuthorizationStart> BeginAsync(
        string baseUrl, string? clientId, string clientDescription, string? signInProfile,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceAuthorizationConfiguration settings = _configuration.DeviceAuthorization;

        string deviceCode = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string userCode = string.Empty;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string candidate = MintUserCode(settings.UserCodeLength);
            if (await _repository.FindByUserCodeHashAsync(
                    IdentityHashing.Hash(NormalizeUserCode(candidate)), cancellationToken) is null)
            {
                userCode = candidate;
                break;
            }
        }
        if (userCode.Length == 0)
            throw new InvalidOperationException("Could not allocate a unique device user code.");

        LoginRequestDocument request = new()
        {
            Id = IdentityHashing.Hash(deviceCode),
            Kind = LoginRequestKinds.Device,
            State = LoginRequestStates.Pending,
            DeviceCodeHash = IdentityHashing.Hash(deviceCode),
            UserCodeHash = IdentityHashing.Hash(NormalizeUserCode(userCode)),
            ClientId = clientId,
            ClientDescription = clientDescription,
            SignInProfile = signInProfile,
            HandoffRequestId = Guid.NewGuid().ToString(),
            PollIntervalSeconds = settings.PollIntervalSeconds,
            CreatedOn = now,
            ExpiresOn = now + settings.CodeLifetime
        };

        DocumentExpiry.Recompute(request, now);
        await _repository.CreateAsync(request, cancellationToken);

        string verificationUri = $"{baseUrl.TrimEnd('/')}{settings.VerificationPath}";

        return new DeviceAuthorizationStart
        {
            DeviceCode = deviceCode,
            UserCode = FormatUserCode(userCode),
            VerificationUri = verificationUri,
            VerificationUriComplete = $"{verificationUri}?user_code={userCode}",
            ExpiresInSeconds = (int)settings.CodeLifetime.TotalSeconds,
            IntervalSeconds = settings.PollIntervalSeconds
        };
    }

    /// <summary>
    /// One poll from the device. Approved requests are consumed atomically — exactly one poll
    /// ever receives <see cref="DevicePollOutcomes.Approved"/> with the user id.
    /// </summary>
    public async Task<DevicePollResult> PollAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        LoginRequestDocument? request = await _repository.GetAsync(IdentityHashing.Hash(deviceCode), cancellationToken);

        if (request is null || request.Kind != LoginRequestKinds.Device)
            return new DevicePollResult { Outcome = DevicePollOutcomes.ExpiredToken };

        if (DocumentExpiry.IsExpired(request, now) || request.State == LoginRequestStates.Expired)
            return new DevicePollResult { Outcome = DevicePollOutcomes.ExpiredToken };

        if (request.State == LoginRequestStates.Denied || request.State == LoginRequestStates.Consumed)
            return new DevicePollResult { Outcome = DevicePollOutcomes.AccessDenied };

        // Interval enforcement: a poll arriving before the interval elapsed is a violation.
        if (request.LastPolledOn is { } lastPolled && (now - lastPolled).TotalSeconds < request.PollIntervalSeconds)
        {
            request.AttemptCount++;
            request.LastPolledOn = now;

            if (request.AttemptCount > _configuration.DeviceAuthorization.MaxPollViolations)
            {
                request.State = LoginRequestStates.Denied;
                DocumentExpiry.Recompute(request, now);
                await _repository.TryReplaceAsync(request, cancellationToken);
                return new DevicePollResult { Outcome = DevicePollOutcomes.AccessDenied };
            }

            DocumentExpiry.Recompute(request, now);
            await _repository.TryReplaceAsync(request, cancellationToken);
            return new DevicePollResult { Outcome = DevicePollOutcomes.SlowDown };
        }

        if (request.State == LoginRequestStates.Approved)
        {
            Guid? approvedUserId = ParseUserId(request.UserId);
            if (approvedUserId is null ||
                !Guid.TryParse(request.HandoffRequestId, out Guid handoffRequestId))
                return new DevicePollResult { Outcome = DevicePollOutcomes.AccessDenied };

            LoginRequestDocument handoff = new()
            {
                Id = handoffRequestId.ToString(),
                Kind = LoginRequestKinds.Login,
                State = LoginRequestStates.Pending,
                UserId = approvedUserId.Value.ToString(),
                CreatedOn = now,
                ExpiresOn = request.ExpiresOn is DateTimeOffset deviceExpiry &&
                    deviceExpiry < now + _configuration.LoginRequestLifetime
                        ? deviceExpiry
                        : now + _configuration.LoginRequestLifetime
            };
            DocumentExpiry.Recompute(handoff, now);

            try
            {
                await _repository.CreateAsync(handoff, cancellationToken);
            }
            catch (CoreConflictException)
            {
                LoginRequestDocument? existing =
                    await _repository.GetAsync(handoff.Id, cancellationToken);
                if (existing?.Kind != LoginRequestKinds.Login ||
                    existing.UserId != handoff.UserId ||
                    DocumentExpiry.IsExpired(existing))
                    return new DevicePollResult { Outcome = DevicePollOutcomes.AccessDenied };
            }

            // Consume atomically: only the winner of this conditional replace gets the identity.
            request.State = LoginRequestStates.Consumed;
            request.LastPolledOn = now;
            DocumentExpiry.Recompute(request, now);

            if (!await _repository.TryReplaceAsync(request, cancellationToken))
                return new DevicePollResult { Outcome = DevicePollOutcomes.AccessDenied };

            await _audit.LogAsync("Device.Consumed", ParseUserId(request.UserId), cancellationToken: cancellationToken);

            return new DevicePollResult
            {
                Outcome = DevicePollOutcomes.Approved,
                UserId = approvedUserId,
                RequestId = handoffRequestId,
                SignInProfile = request.SignInProfile
            };
        }

        request.LastPolledOn = now;
        DocumentExpiry.Recompute(request, now);
        if (!await _repository.TryReplaceAsync(request, cancellationToken))
            return new DevicePollResult { Outcome = DevicePollOutcomes.SlowDown };

        return new DevicePollResult { Outcome = DevicePollOutcomes.AuthorizationPending };
    }

    /// <summary>The approval page resolves a user code to the request the person is confirming.</summary>
    public async Task<DeviceApprovalView?> GetPendingByUserCodeAsync(string userCode, CancellationToken cancellationToken = default)
    {
        LoginRequestDocument? request = await FindPendingAsync(userCode, cancellationToken);

        if (request is null)
            return null;

        return new DeviceApprovalView
        {
            UserCode = FormatUserCode(NormalizeUserCode(userCode)),
            ClientDescription = request.ClientDescription ?? "Unknown device",
            ExpiresOn = request.ExpiresOn!.Value
        };
    }

    /// <summary>
    /// Approves a pending request. Requires an authenticated user who explicitly confirmed the
    /// requesting client — both enforced by the caller before this runs.
    /// <para>
    /// <paramref name="approvingMethod"/> is how the approving person signed in on the device
    /// they are approving from. A QR request carries the sign-in profile the TV asked for, and
    /// that profile's allowed methods govern the approval: a profile that permits only a passkey
    /// must not be satisfiable by approving from a session that was itself started with a
    /// password. Null skips the check, for callers that have no method to report.
    /// </para>
    /// </summary>
    public async Task<bool> ApproveAsync(
        string userCode, Guid approvingUserId, string? approvingMethod = null,
        CancellationToken cancellationToken = default)
    {
        if (approvingMethod is not null && !await ApprovalMethodAllowedAsync(userCode, approvingMethod, cancellationToken))
            return false;

        return await DecideAsync(userCode, LoginRequestStates.Approved, approvingUserId, cancellationToken);
    }

    /// <summary>
    /// Whether the method the approver signed in with satisfies the profile the pending request
    /// was started under. Unknown requests answer true so the caller's own not-found handling
    /// stays the single place that decides what a missing request means.
    /// </summary>
    private async Task<bool> ApprovalMethodAllowedAsync(string userCode, string approvingMethod, CancellationToken cancellationToken)
    {
        LoginRequestDocument? request = await FindPendingAsync(userCode, cancellationToken);

        if (request?.SignInProfile is not { } profileName || _signInProfiles is null)
            return true;

        CloudLoginSignInProfile? profile = _signInProfiles.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase));

        return profile is null || SignInProfileService.AllowsMethod(profile, approvingMethod);
    }

    public Task<bool> DenyAsync(string userCode, Guid decidingUserId, CancellationToken cancellationToken = default) =>
        DecideAsync(userCode, LoginRequestStates.Denied, decidingUserId, cancellationToken);

    private async Task<bool> DecideAsync(string userCode, LoginRequestStates decision, Guid userId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LoginRequestDocument? request = await FindPendingAsync(userCode, cancellationToken);

        if (request is null)
            return false;

        request.State = decision;
        request.ApprovedByUserId = userId.ToString();
        request.ApprovedOn = now;

        if (decision == LoginRequestStates.Approved)
            request.UserId = userId.ToString();

        DocumentExpiry.Recompute(request, now);

        bool won = await _repository.TryReplaceAsync(request, cancellationToken);

        if (won)
            await _audit.LogAsync($"Device.{decision}", userId, cancellationToken: cancellationToken);

        return won;
    }

    private async Task<LoginRequestDocument?> FindPendingAsync(string userCode, CancellationToken cancellationToken)
    {
        string userCodeHash = IdentityHashing.Hash(NormalizeUserCode(userCode));
        LoginRequestDocument? request = await _repository.FindByUserCodeHashAsync(userCodeHash, cancellationToken);

        if (request is null || request.State != LoginRequestStates.Pending || DocumentExpiry.IsExpired(request))
            return null;

        return request;
    }

    private static string MintUserCode(int length)
    {
        char[] characters = new char[length];

        for (int index = 0; index < length; index++)
            characters[index] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];

        return new string(characters);
    }

    /// <summary>Case-insensitive, hyphen/space-insensitive comparison form of a user code.</summary>
    private static string NormalizeUserCode(string userCode) =>
        new([.. userCode.ToUpperInvariant().Where(char.IsLetterOrDigit)]);

    private static string FormatUserCode(string userCode) =>
        userCode.Length >= 6 ? $"{userCode[..(userCode.Length / 2)]}-{userCode[(userCode.Length / 2)..]}" : userCode;

    private static Guid? ParseUserId(string? userId) =>
        Guid.TryParse(userId, out Guid parsed) ? parsed : null;
}
