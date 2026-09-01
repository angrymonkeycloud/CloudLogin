using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class IdentityLinkingTests
{
    private readonly InMemoryIdentityKeyStore _identityKeys = new(TestIdentityHmac.Hasher);
    private readonly InMemoryCredentialRepository _credentials = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly IdentityLinkingService _service;

    private static readonly LinkingProof ValidProof = new() { RecentAuthentication = true, ProviderProofPresented = true };

    public IdentityLinkingTests() => _service = new IdentityLinkingService(
        _identityKeys, _credentials, _users, _configuration, new AuditLogger(_audit, _configuration));

    private async Task<Guid> AddUserAsync()
    {
        Guid userId = Guid.NewGuid();
        await _users.CreateAsync(new UserDocument { Id = userId.ToString() });
        return userId;
    }

    private static IdentityReservation VerifiedEmail(string address, Guid? contactId = null) => new()
    {
        Type = IdentityKeyTypes.Email,
        CanonicalValue = IdentityKey.CanonicalEmail(address),
        ContactId = contactId,
        IsVerified = true
    };

    private Task<bool> ClaimVerifiedEmailAsync(Guid userId, string address, Guid? contactId = null) =>
        _service.ClaimIdentityAsync(VerifiedEmail(address, contactId), userId);

    private Task LinkGoogleAsync(
        Guid userId, string subject, string? providerEmail = null, bool providerEmailIsVerified = false,
        Guid? contactId = null, LinkingProof? proof = null) =>
        _service.LinkExternalIdentityAsync(
            userId, "https://accounts.google.com", subject, "Google",
            providerEmail, providerEmailIsVerified, contactId, proof ?? ValidProof);

    // ── Identity uniqueness ───────────────────────────────────────────────────

    [Fact]
    public async Task ClaimIdentity_SecondUser_ConflictsInsteadOfOverwriting()
    {
        Guid firstUser = Guid.NewGuid();
        Guid secondUser = Guid.NewGuid();

        await ClaimVerifiedEmailAsync(firstUser, "ada@example.com");

        await Assert.ThrowsAsync<IdentityAlreadyLinkedException>(
            () => ClaimVerifiedEmailAsync(secondUser, "ada@example.com"));

        Assert.Equal(firstUser,
            (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")))!.UserId);
    }

    [Fact]
    public async Task ClaimIdentity_SameUserTwice_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();

        Assert.True(await ClaimVerifiedEmailAsync(userId, "ada@example.com"));
        Assert.False(await ClaimVerifiedEmailAsync(userId, "ada@example.com"));

        Assert.Single(_identityKeys.Keys);
    }

    [Fact]
    public async Task ClaimIdentity_RecordsTheContactItBelongsTo()
    {
        Guid userId = Guid.NewGuid();
        Guid contactId = Guid.NewGuid();

        await ClaimVerifiedEmailAsync(userId, "ada@example.com", contactId);

        IdentityKey key = (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")))!;
        Assert.Equal(contactId, key.ContactId);
        Assert.Equal(IdentityKeyTypes.Email, key.Type);
    }

    // ── Verification before reservation ───────────────────────────────────────

    [Theory]
    [InlineData(IdentityKeyTypes.Email, "email:ada@example.com")]
    [InlineData(IdentityKeyTypes.Phone, "phone:+15551234567")]
    public async Task ClaimIdentity_UnverifiedContact_Refused(IdentityKeyTypes type, string canonicalValue)
    {
        Guid userId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnverifiedIdentityException>(() => _service.ClaimIdentityAsync(
            new IdentityReservation { Type = type, CanonicalValue = canonicalValue, IsVerified = false }, userId));

        // Nothing was reserved, so the real owner can still claim it later.
        Assert.Empty(_identityKeys.Keys);
    }

    [Fact]
    public async Task ClaimIdentity_UnverifiedExternalIdentity_Allowed()
    {
        // A completed provider flow is itself the verification, so external reservations do not
        // carry a separate verified flag to satisfy.
        Guid userId = Guid.NewGuid();

        await _service.ClaimIdentityAsync(new IdentityReservation
        {
            Type = IdentityKeyTypes.External,
            CanonicalValue = IdentityKey.CanonicalExternal("https://accounts.google.com", "sub-0"),
            IsVerified = false
        }, userId);

        Assert.Single(_identityKeys.Keys);
    }

    // ── External sign-in policy ───────────────────────────────────────────────

    [Fact]
    public async Task EvaluateExternalSignIn_LinkedSubject_SignsInThatUser()
    {
        Guid userId = await AddUserAsync();
        await LinkGoogleAsync(userId, "sub-1", "ada@example.com", providerEmailIsVerified: true);

        ExternalSignInEvaluation evaluation = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-1", "different@example.com", providerEmailIsVerified: true);

        // The subject decides, not the email — a changed provider email still signs the same person in.
        Assert.Equal(ExternalSignInDecisions.SignInLinkedUser, evaluation.Decision);
        Assert.Equal(userId, evaluation.UserId);
    }

    [Fact]
    public async Task EvaluateExternalSignIn_NoLinkNoEmailMatch_RegistersNewUser()
    {
        ExternalSignInEvaluation evaluation = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-unknown", "new@example.com", providerEmailIsVerified: true);

        Assert.Equal(ExternalSignInDecisions.RegisterNewUser, evaluation.Decision);
    }

    [Fact]
    public async Task EvaluateExternalSignIn_UnverifiedEmail_NeverLinks()
    {
        Guid emailOwner = Guid.NewGuid();
        await ClaimVerifiedEmailAsync(emailOwner, "ada@example.com");

        // The provider did not verify the email, so it must not even suggest linking.
        ExternalSignInEvaluation withoutEmail = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-2", providerEmail: null, providerEmailIsVerified: false);
        Assert.Equal(ExternalSignInDecisions.RegisterNewUser, withoutEmail.Decision);

        // An email the provider reports but has not verified is ignored just as completely.
        ExternalSignInEvaluation unverified = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-2", "ada@example.com", providerEmailIsVerified: false);
        Assert.Equal(ExternalSignInDecisions.RegisterNewUser, unverified.Decision);
        Assert.Null(unverified.UserId);
    }

    [Fact]
    public async Task EvaluateExternalSignIn_SameVerifiedEmail_RequiresCeremonyByDefault()
    {
        Guid emailOwner = Guid.NewGuid();
        await ClaimVerifiedEmailAsync(emailOwner, "ada@example.com");

        ExternalSignInEvaluation evaluation = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-2", "ada@example.com", providerEmailIsVerified: true);

        Assert.Equal(ExternalSignInDecisions.RequireLinkingCeremony, evaluation.Decision);
        Assert.Equal(emailOwner, evaluation.UserId);
    }

    [Fact]
    public async Task EvaluateExternalSignIn_TrustedIssuer_MayAutoLinkOnlyWhenEnabled()
    {
        Guid emailOwner = Guid.NewGuid();
        await ClaimVerifiedEmailAsync(emailOwner, "ada@example.com");

        _configuration.IdentityLinking.TrustedAutoLinkIssuers.Add("https://accounts.google.com");

        // Listed but the switch is off (the default): still a ceremony.
        ExternalSignInEvaluation disabled = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-2", "ada@example.com", providerEmailIsVerified: true);
        Assert.Equal(ExternalSignInDecisions.RequireLinkingCeremony, disabled.Decision);

        _configuration.IdentityLinking.AllowTrustedIssuerAutoLink = true;

        ExternalSignInEvaluation enabled = await _service.EvaluateExternalSignInAsync(
            "https://accounts.google.com", "sub-2", "ada@example.com", providerEmailIsVerified: true);
        Assert.Equal(ExternalSignInDecisions.AutoLink, enabled.Decision);
    }

    // ── Linking rules ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Link_WithoutRecentAuthenticationOrProof_Refused()
    {
        Guid userId = await AddUserAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => LinkGoogleAsync(userId, "sub-3",
            proof: new LinkingProof { RecentAuthentication = false, ProviderProofPresented = true }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => LinkGoogleAsync(userId, "sub-3",
            proof: new LinkingProof { RecentAuthentication = true, ProviderProofPresented = false }));

        Assert.Empty(_identityKeys.Keys);
    }

    [Fact]
    public async Task Link_IdentityOwnedByAnotherUser_Refused()
    {
        Guid firstUser = await AddUserAsync();
        Guid secondUser = await AddUserAsync();

        await LinkGoogleAsync(firstUser, "sub-4");

        await Assert.ThrowsAsync<IdentityAlreadyLinkedException>(() => LinkGoogleAsync(secondUser, "sub-4"));
    }

    [Fact]
    public async Task Link_SameSubjectFromADifferentIssuer_IsADifferentIdentity()
    {
        Guid firstUser = await AddUserAsync();
        Guid secondUser = await AddUserAsync();

        await LinkGoogleAsync(firstUser, "shared-subject");

        // Provider subjects are only unique within their own issuer, so the pair is what has to
        // be unique — not the subject on its own.
        await _service.LinkExternalIdentityAsync(
            secondUser, "https://login.microsoftonline.com/common/v2.0", "shared-subject", "Microsoft",
            null, false, null, ValidProof);

        Assert.Equal(firstUser, (await _identityKeys.ResolveAsync("default",
            IdentityKey.CanonicalExternal("https://accounts.google.com", "shared-subject")))!.UserId);
        Assert.Equal(secondUser, (await _identityKeys.ResolveAsync("default",
            IdentityKey.CanonicalExternal("https://login.microsoftonline.com/common/v2.0", "shared-subject")))!.UserId);
    }

    [Fact]
    public async Task Link_StoresProviderEmailAndVerifiedStatusOnTheCredential()
    {
        Guid userId = await AddUserAsync();
        Guid contactId = Guid.NewGuid();

        await LinkGoogleAsync(userId, "sub-8", "Ada@Example.com", providerEmailIsVerified: true, contactId: contactId);

        CredentialDocument credential = (await _credentials.GetAllForUserAsync(userId))
            .Single(candidate => candidate.Kind == CredentialKinds.ExternalIdentity);

        Assert.Equal("ada@example.com", credential.ProviderEmail);
        Assert.True(credential.ProviderEmailIsVerified);
        Assert.Equal(contactId, credential.LinkedContactId);
        Assert.Equal("https://accounts.google.com", credential.Issuer);
    }

    [Fact]
    public async Task Link_AuditTrail_NeverRecordsTheProviderSubject()
    {
        Guid userId = await AddUserAsync();
        await LinkGoogleAsync(userId, "highly-identifying-subject", "ada@example.com", providerEmailIsVerified: true);

        // A second sign-in method, so the unlink is not refused by the final-credential guard.
        await _credentials.UpsertAsync(new CredentialDocument
        {
            Id = CredentialDocument.PasswordId(Guid.NewGuid()),
            UserId = userId.ToString(),
            Kind = CredentialKinds.Password,
            PasswordHash = "hash"
        });

        await _service.UnlinkExternalIdentityAsync(userId, "https://accounts.google.com", "highly-identifying-subject");

        // The subject is what the identity index is keyed on and a cross-service correlator for a
        // real person. The issuer and provider code are enough to read the trail.
        Assert.NotEmpty(_audit.Events);
        Assert.All(_audit.Events, auditEvent =>
            Assert.DoesNotContain(auditEvent.Data ?? [], entry =>
                entry.Value.Contains("highly-identifying-subject", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Link_RotatesSecurityStamp()
    {
        Guid userId = await AddUserAsync();
        string stampBefore = (await _users.GetAsync(userId))!.SecurityStamp;

        await LinkGoogleAsync(userId, "sub-5");

        Assert.NotEqual(stampBefore, (await _users.GetAsync(userId))!.SecurityStamp);
    }

    [Fact]
    public async Task Unlink_FinalSignInMethod_Refused()
    {
        Guid userId = await AddUserAsync();
        await LinkGoogleAsync(userId, "sub-6");

        await Assert.ThrowsAsync<FinalSignInMethodException>(
            () => _service.UnlinkExternalIdentityAsync(userId, "https://accounts.google.com", "sub-6"));
    }

    [Fact]
    public async Task Unlink_WithAnotherMethodRemaining_Succeeds()
    {
        Guid userId = await AddUserAsync();
        Guid contactId = Guid.NewGuid();

        await LinkGoogleAsync(userId, "sub-7");
        await _credentials.UpsertAsync(new CredentialDocument
        {
            Id = CredentialDocument.PasswordId(contactId),
            UserId = userId.ToString(),
            Kind = CredentialKinds.Password,
            ContactId = contactId,
            PasswordHash = "hash"
        });

        await _service.UnlinkExternalIdentityAsync(userId, "https://accounts.google.com", "sub-7");

        Assert.Null(await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalExternal("https://accounts.google.com", "sub-7")));
    }

    // ── Registration saga ─────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterNewUser_FirstUserWinsBootstrapAdminExactlyOnce()
    {
        UserDocument first = new() { Id = Guid.NewGuid().ToString() };
        UserDocument second = new() { Id = Guid.NewGuid().ToString() };

        await _service.RegisterNewUserAsync(first, [VerifiedEmail("a@example.com")], []);
        await _service.RegisterNewUserAsync(second, [VerifiedEmail("b@example.com")], []);

        Assert.True(first.IsGlobalAdmin);
        Assert.False(second.IsGlobalAdmin);
    }

    [Fact]
    public async Task RegisterNewUser_FailureReleasesClaimedIdentities()
    {
        // Claim the second identity for someone else so the saga fails mid-claim.
        Guid otherUser = Guid.NewGuid();
        await ClaimVerifiedEmailAsync(otherUser, "taken@example.com");

        UserDocument user = new() { Id = Guid.NewGuid().ToString() };

        await Assert.ThrowsAsync<IdentityAlreadyLinkedException>(() => _service.RegisterNewUserAsync(user,
        [
            VerifiedEmail("fresh@example.com"),
            VerifiedEmail("taken@example.com")
        ], []));

        // Compensation released the claim this saga made; the other user's claim stands.
        Assert.Null(await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("fresh@example.com")));
        Assert.Equal(otherUser, (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("taken@example.com")))!.UserId);

        // And no user document was written.
        Assert.Empty(_users.Documents);
    }

    [Fact]
    public async Task RegisterNewUser_UnverifiedIdentity_WritesNothing()
    {
        UserDocument user = new() { Id = Guid.NewGuid().ToString() };

        await Assert.ThrowsAsync<UnverifiedIdentityException>(() => _service.RegisterNewUserAsync(user,
        [
            new IdentityReservation
            {
                Type = IdentityKeyTypes.Email,
                CanonicalValue = IdentityKey.CanonicalEmail("unverified@example.com"),
                IsVerified = false
            }
        ], []));

        Assert.Empty(_identityKeys.Keys);
        Assert.Empty(_users.Documents);
    }
}
