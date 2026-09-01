using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>The identity is already claimed by a different user.</summary>
public sealed class IdentityAlreadyLinkedException(string message) : InvalidOperationException(message);

/// <summary>The operation would remove the user's final usable sign-in method.</summary>
public sealed class FinalSignInMethodException() :
    InvalidOperationException("This is the account's only remaining sign-in method and cannot be removed.");

/// <summary>
/// An unverified email or phone number cannot be reserved in the identity index.
/// <para>
/// The reservation is permanent and exclusive: whoever claims <c>ada@example.com</c> owns it for
/// every future sign-in. Claiming on an unverified value would let anyone who can type an address
/// lock its real owner out of ever registering it.
/// </para>
/// </summary>
public sealed class UnverifiedIdentityException(string identityType) : InvalidOperationException(
    $"A {identityType} must be verified before it can be reserved as a sign-in identity.");

/// <summary>What an external provider callback should do next.</summary>
public enum ExternalSignInDecisions
{
    /// <summary>The (issuer, subject) pair is already linked: sign the linked user in.</summary>
    SignInLinkedUser,

    /// <summary>No link and no email match: register a new account.</summary>
    RegisterNewUser,

    /// <summary>
    /// Another account owns the same verified email. Default outcome: the person must sign in to
    /// that account first and approve the link (authenticated linking ceremony).
    /// </summary>
    RequireLinkingCeremony,

    /// <summary>Same verified email and the issuer is explicitly trusted for automatic linking.</summary>
    AutoLink
}

public sealed record ExternalSignInEvaluation
{
    public required ExternalSignInDecisions Decision { get; init; }
    public Guid? UserId { get; init; }
}

/// <summary>Proof the caller gathered before a link may be written.</summary>
public sealed record LinkingProof
{
    /// <summary>The acting user re-authenticated recently (freshness enforced by the caller's policy).</summary>
    public required bool RecentAuthentication { get; init; }

    /// <summary>The provider completed its flow in this session and asserted the (issuer, subject) pair.</summary>
    public required bool ProviderProofPresented { get; init; }
}

/// <summary>
/// One identity being reserved for a user, with everything the reservation needs: what it is,
/// which contact it belongs to, and whether it has been verified.
/// </summary>
public sealed record IdentityReservation
{
    public required IdentityKeyTypes Type { get; init; }
    public required string CanonicalValue { get; init; }

    /// <summary>The contact this identity belongs to. Required for email and phone, optional for external.</summary>
    public Guid? ContactId { get; init; }

    /// <summary>
    /// Whether ownership has been proven — a delivered verification code for email and phone, a
    /// completed provider flow for external. Claiming an unverified email or phone is refused.
    /// </summary>
    public required bool IsVerified { get; init; }
}

/// <summary>
/// External identities as <c>(realm, issuer, subject)</c> — never email alone — plus the
/// claim/release lifecycle of every identity key. All writes to the identity index are
/// create-only, so two accounts can never end up owning the same identity, and cross-store
/// user creation runs as a reservation saga with compensation.
/// </summary>
public sealed class IdentityLinkingService(
    IIdentityKeyStore identityKeys,
    ICredentialRepository credentials,
    IUserRepository users,
    CloudLoginCoreConfiguration configuration,
    IAuditLogger audit)
{
    private readonly IIdentityKeyStore _identityKeys = identityKeys;
    private readonly ICredentialRepository _credentials = credentials;
    private readonly IUserRepository _users = users;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly IAuditLogger _audit = audit;

    private string Realm => _configuration.RealmId;

    // ── Resolution ────────────────────────────────────────────────────────────

    public async Task<Guid?> ResolveUserIdAsync(string canonicalValue, CancellationToken cancellationToken = default)
    {
        IdentityKey? key = await _identityKeys.ResolveAsync(Realm, canonicalValue, cancellationToken);
        return key?.UserId;
    }

    /// <summary>Resolves an identity to the user and contact that own it. Null when unclaimed.</summary>
    public Task<IdentityKey?> ResolveAsync(string canonicalValue, CancellationToken cancellationToken = default) =>
        _identityKeys.ResolveAsync(Realm, canonicalValue, cancellationToken);

    // ── External sign-in policy ───────────────────────────────────────────────

    /// <summary>
    /// Decides what an external provider callback does with an asserted identity.
    /// <para>
    /// An unverified provider email never links: it is ignored entirely, and the callback falls
    /// through to registering a new account rather than reaching for an existing one. Email is
    /// never the thing that identifies an external account — <paramref name="issuer"/> and
    /// <paramref name="subject"/> are.
    /// </para>
    /// </summary>
    public async Task<ExternalSignInEvaluation> EvaluateExternalSignInAsync(
        string issuer, string subject, string? providerEmail, bool providerEmailIsVerified,
        CancellationToken cancellationToken = default)
    {
        Guid? linkedUserId = await ResolveUserIdAsync(IdentityKey.CanonicalExternal(issuer, subject), cancellationToken);
        if (linkedUserId is not null)
            return new ExternalSignInEvaluation { Decision = ExternalSignInDecisions.SignInLinkedUser, UserId = linkedUserId };

        if (!providerEmailIsVerified || string.IsNullOrWhiteSpace(providerEmail))
            return new ExternalSignInEvaluation { Decision = ExternalSignInDecisions.RegisterNewUser };

        string normalizedEmail = IdentityNormalization.NormalizeEmail(providerEmail);
        Guid? emailOwnerId = await ResolveUserIdAsync(IdentityKey.CanonicalEmail(normalizedEmail), cancellationToken);

        if (emailOwnerId is null)
            return new ExternalSignInEvaluation { Decision = ExternalSignInDecisions.RegisterNewUser };

        IdentityLinkingConfiguration linking = _configuration.IdentityLinking;
        bool trusted = linking.AllowTrustedIssuerAutoLink
            && linking.TrustedAutoLinkIssuers.Contains(issuer, StringComparer.OrdinalIgnoreCase);

        return new ExternalSignInEvaluation
        {
            Decision = trusted ? ExternalSignInDecisions.AutoLink : ExternalSignInDecisions.RequireLinkingCeremony,
            UserId = emailOwnerId
        };
    }

    // ── Linking / unlinking ───────────────────────────────────────────────────

    /// <summary>
    /// Links an external identity to a user. Requires recent authentication on the account being
    /// linked to and completed provider proof — the two halves of "this person owns both sides".
    /// Refuses identities already owned by another account, and never merges on a matching email.
    /// </summary>
    public async Task LinkExternalIdentityAsync(
        Guid userId, string issuer, string subject, string providerCode,
        string? providerEmail, bool providerEmailIsVerified, Guid? linkedContactId,
        LinkingProof proof, CancellationToken cancellationToken = default)
    {
        if (!proof.RecentAuthentication || !proof.ProviderProofPresented)
            throw new InvalidOperationException("Linking requires recent authentication and completed provider proof.");

        string canonical = IdentityKey.CanonicalExternal(issuer, subject);

        // The provider flow completing is itself the verification of the external identity, so
        // this reservation is verified by construction — unlike an email or phone, which needs a
        // delivered code before anything permanent is written.
        bool claimed = await ClaimIdentityAsync(
            new IdentityReservation
            {
                Type = IdentityKeyTypes.External,
                CanonicalValue = canonical,
                ContactId = linkedContactId,
                IsVerified = true
            },
            userId, cancellationToken);

        CredentialDocument credential = new()
        {
            Id = CredentialDocument.ExternalIdentityId(issuer, subject),
            UserId = userId.ToString(),
            Kind = CredentialKinds.ExternalIdentity,
            Issuer = issuer,
            Subject = subject,
            ProviderCode = providerCode,
            LinkedContactId = linkedContactId,
            ProviderEmail = providerEmail is null ? null : IdentityNormalization.NormalizeEmail(providerEmail),
            ProviderEmailIsVerified = providerEmailIsVerified,
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        };

        try
        {
            await _credentials.UpsertAsync(credential, cancellationToken);
        }
        catch
        {
            if (claimed)
                await _identityKeys.DeleteIfOwnedAsync(Realm, canonical, userId, cancellationToken);
            throw;
        }

        await RotateSecurityStampAsync(userId, cancellationToken);

        // Provider and issuer only. The subject is the provider's stable identifier for this
        // person and never appears in a log line.
        await _audit.LogAsync("Identity.Linked", userId,
            data: new Dictionary<string, string> { ["Issuer"] = issuer, ["Provider"] = providerCode },
            cancellationToken: cancellationToken);
    }

    /// <summary>Unlinks an external identity, refusing to strand the account without any sign-in method.</summary>
    public async Task UnlinkExternalIdentityAsync(Guid userId, string issuer, string subject, CancellationToken cancellationToken = default)
    {
        await EnsureNotFinalMethodAsync(userId, CredentialDocument.ExternalIdentityId(issuer, subject), cancellationToken);

        await _identityKeys.DeleteIfOwnedAsync(
            Realm, IdentityKey.CanonicalExternal(issuer, subject), userId, cancellationToken);
        await _credentials.DeleteAsync(userId, CredentialDocument.ExternalIdentityId(issuer, subject), cancellationToken);
        await RotateSecurityStampAsync(userId, cancellationToken);
        await _audit.LogAsync("Identity.Unlinked", userId,
            data: new Dictionary<string, string> { ["Issuer"] = issuer },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Claims an identity key for a user with a create-only insert. Idempotent for the same
    /// user; a claim held by anyone else throws <see cref="IdentityAlreadyLinkedException"/>.
    /// An unverified email or phone is refused outright — the reservation is permanent.
    /// </summary>
    public async Task<bool> ClaimIdentityAsync(
        IdentityReservation reservation, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!reservation.IsVerified && reservation.Type is IdentityKeyTypes.Email or IdentityKeyTypes.Phone)
            throw new UnverifiedIdentityException(reservation.Type == IdentityKeyTypes.Email ? "email address" : "phone number");

        IdentityKeyClaim claim = new()
        {
            Type = reservation.Type,
            CanonicalValue = reservation.CanonicalValue,
            UserId = userId,
            ContactId = reservation.ContactId
        };

        try
        {
            await _identityKeys.InsertAsync(Realm, claim, cancellationToken);
            return true;
        }
        catch (CoreConflictException)
        {
            IdentityKey? existing = await _identityKeys.ResolveAsync(Realm, reservation.CanonicalValue, cancellationToken);

            if (existing is null)
                throw; // Deleted between insert and read; extremely rare — let the caller retry.

            if (existing.UserId != userId)
                throw new IdentityAlreadyLinkedException("This identity is already connected to another account.");

            return false;
        }
    }

    public Task<bool> ReleaseIdentityAsync(
        Guid expectedUserId, string canonicalValue, CancellationToken cancellationToken = default) =>
        _identityKeys.DeleteIfOwnedAsync(Realm, canonicalValue, expectedUserId, cancellationToken);

    // ── Registration saga ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a user across Cosmos and Table Storage as a reservation saga: identity keys are
    /// claimed first (create-only — the reservation), then the user document and credentials are
    /// written; any failure releases every key this call claimed. The first-administrator grant
    /// is an atomic bootstrap reservation, so two racing first registrations can never both
    /// become the administrator.
    /// </summary>
    public async Task RegisterNewUserAsync(
        UserDocument user, IReadOnlyList<IdentityReservation> identities,
        IReadOnlyList<CredentialDocument> newCredentials, CancellationToken cancellationToken = default)
    {
        Guid userId = Guid.Parse(user.Id);
        List<string> claimed = [];
        bool bootstrapReserved = false;

        try
        {
            foreach (IdentityReservation reservation in identities)
            {
                if (await ClaimIdentityAsync(reservation, userId, cancellationToken))
                    claimed.Add(reservation.CanonicalValue);
            }

            if (!user.IsGlobalAdmin &&
                (bootstrapReserved = await _identityKeys.TryReserveBootstrapAsync(
                    Realm, "global-admin", userId, cancellationToken)))
                user.IsGlobalAdmin = true;

            await _users.CreateAsync(user, cancellationToken);

            foreach (CredentialDocument credential in newCredentials)
                await _credentials.UpsertAsync(credential, cancellationToken);
        }
        catch
        {
            // Compensation: release only the reservations this call made.
            foreach (string canonicalValue in claimed)
            {
                try { await _identityKeys.DeleteIfOwnedAsync(Realm, canonicalValue, userId, cancellationToken); }
                catch { /* Reconciliation cleans up what compensation could not. */ }
            }

            if (bootstrapReserved)
            {
                try { await _identityKeys.ReleaseBootstrapAsync(Realm, "global-admin", userId, cancellationToken); }
                catch { /* Reconciliation cleans up what compensation could not. */ }
            }

            throw;
        }

        await _audit.LogAsync("User.Registered", userId, cancellationToken: cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Throws when removing <paramref name="credentialIdBeingRemoved"/> would leave no usable sign-in method.</summary>
    public async Task EnsureNotFinalMethodAsync(Guid userId, string credentialIdBeingRemoved, CancellationToken cancellationToken = default)
    {
        List<CredentialDocument> all = await _credentials.GetAllForUserAsync(userId, cancellationToken);

        int usable = all.Count(credential =>
            credential.Kind is CredentialKinds.Password or CredentialKinds.Passkey or CredentialKinds.ExternalIdentity
            && !string.Equals(credential.Id, credentialIdBeingRemoved, StringComparison.Ordinal));

        if (usable == 0)
            throw new FinalSignInMethodException();
    }

    private async Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        UserDocument? user = await _users.GetAsync(userId, cancellationToken);
        if (user is null)
            return;

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedOn = DateTimeOffset.UtcNow;

        try
        {
            await _users.ReplaceAsync(user, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            UserDocument? current = await _users.GetAsync(userId, cancellationToken);
            if (current is null)
                return;

            current.SecurityStamp = Guid.NewGuid().ToString("N");
            current.UpdatedOn = DateTimeOffset.UtcNow;
            await _users.ReplaceAsync(current, cancellationToken);
        }
    }
}
