using System.Buffers.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// WebAuthn (passkey) registration and assertion, covering platform authenticators such as
/// Windows Hello, Touch ID, and Android biometrics as well as roaming security keys.
/// <para>
/// All cryptographic work — attestation parsing, COSE key handling, signature and origin
/// verification — is delegated to Fido2NetLib. This class only supplies configuration,
/// persists the resulting credential, and enforces the sign-counter rule.
/// </para>
/// </summary>
public sealed class CloudLoginWebAuthnService(CloudLoginSecurityStore store, CloudLoginSecurityOptions security)
{
    private readonly CloudLoginSecurityStore _store = store;
    private readonly CloudLoginSecurityOptions _security = security;

    /// <summary>
    /// Builds a Fido2 instance bound to the caller's origin. The RP ID defaults to the request
    /// host, which is right for a single-host deployment; a deployment spanning subdomains sets
    /// <see cref="CloudLoginSecurityOptions.WebAuthnRelyingPartyId"/> to the registrable domain.
    /// </summary>
    private IFido2 CreateFido2(Uri origin)
    {
        HashSet<string> origins = new(StringComparer.OrdinalIgnoreCase) { origin.GetLeftPart(UriPartial.Authority) };

        foreach (string extra in _security.WebAuthnAllowedOrigins)
            origins.Add(extra);

        return new Fido2(new Fido2Configuration
        {
            ServerDomain = _security.WebAuthnRelyingPartyId ?? origin.Host,
            ServerName = _security.WebAuthnRelyingPartyName,
            Origins = origins
        });
    }

    /// <summary>
    /// Produces the creation options the browser passes to <c>navigator.credentials.create()</c>.
    /// Existing credentials are excluded so the same authenticator isn't enrolled twice.
    /// </summary>
    public async Task<CredentialCreateOptions> BeginRegistration(UserModel user, Uri origin)
    {
        UserSecurityDocument credentials = await _store.GetCredentials(user.ID);

        Fido2User fidoUser = new()
        {
            Id = user.ID.ToByteArray(),
            Name = GetAccountName(user),
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? GetAccountName(user) : user.DisplayName!
        };

        List<PublicKeyCredentialDescriptor> existing = [.. credentials.Passkeys
            .Select(passkey => new PublicKeyCredentialDescriptor(Base64Url.DecodeFromChars(passkey.CredentialId)))];

        return CreateFido2(origin).RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = existing,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // Require a user-verifying gesture (PIN, fingerprint, face) rather than mere presence
                // — this credential is being registered as a second factor, so proof of the user is
                // the entire point.
                UserVerification = UserVerificationRequirement.Required,
                ResidentKey = ResidentKeyRequirement.Preferred
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
    }

    /// <summary>
    /// Verifies the authenticator's attestation response and stores the resulting credential.
    /// </summary>
    public async Task<PasskeySummary> CompleteRegistration(
        UserModel user,
        Uri origin,
        CredentialCreateOptions originalOptions,
        AuthenticatorAttestationRawResponse attestationResponse,
        string? friendlyName)
    {
        RegisteredPublicKeyCredential credential = await CreateFido2(origin).MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestationResponse,
            OriginalOptions = originalOptions,
            IsCredentialIdUniqueToUserCallback = async (args, _) =>
            {
                // The credential must not already be registered to this account. A global check
                // isn't possible here because credentials are stored per user, and isn't needed:
                // passkeys are used as a second factor once the user is already identified.
                UserSecurityDocument existing = await _store.GetCredentials(user.ID);
                string candidate = Base64Url.EncodeToString(args.CredentialId);

                return !existing.Passkeys.Any(p => p.CredentialId == candidate);
            }
        });

        UserPasskey passkey = new()
        {
            CredentialId = Base64Url.EncodeToString(credential.Id),
            PublicKey = credential.PublicKey,
            SignCount = credential.SignCount,
            AaGuid = credential.AaGuid,
            IsBackedUp = credential.IsBackedUp,
            Transports = [.. (credential.Transports ?? []).Select(t => t.ToString())],
            Name = string.IsNullOrWhiteSpace(friendlyName) ? "Passkey" : friendlyName.Trim(),
            CreatedOn = DateTimeOffset.UtcNow
        };

        await _store.UpdateCredentials(user.ID, document =>
        {
            document.Passkeys.RemoveAll(p => p.CredentialId == passkey.CredentialId);
            document.Passkeys.Add(passkey);
        });

        return ToSummary(passkey);
    }

    /// <summary>
    /// Produces the request options the browser passes to <c>navigator.credentials.get()</c>,
    /// scoped to the credentials this user has registered.
    /// </summary>
    public async Task<AssertionOptions> BeginAssertion(Guid userId, Uri origin)
    {
        UserSecurityDocument credentials = await _store.GetCredentials(userId);

        if (credentials.Passkeys.Count == 0)
            throw new InvalidOperationException("This account has no registered passkeys.");

        List<PublicKeyCredentialDescriptor> allowed = [.. credentials.Passkeys
            .Select(passkey => new PublicKeyCredentialDescriptor(Base64Url.DecodeFromChars(passkey.CredentialId)))];

        return CreateFido2(origin).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowed,
            UserVerification = UserVerificationRequirement.Required
        });
    }

    /// <summary>
    /// Verifies an assertion against the stored credential and advances the signature counter.
    /// </summary>
    /// <returns>True when the assertion is valid.</returns>
    public async Task<bool> CompleteAssertion(
        Guid userId,
        Uri origin,
        AssertionOptions originalOptions,
        AuthenticatorAssertionRawResponse assertionResponse)
    {
        UserSecurityDocument credentials = await _store.GetCredentials(userId);

        // `Id` is already the Base64Url encoding of `RawId`, matching how credentials are stored.
        string credentialId = assertionResponse.Id;
        UserPasskey? stored = credentials.Passkeys.FirstOrDefault(p => p.CredentialId == credentialId);

        if (stored is null)
            return false;

        VerifyAssertionResult result;

        try
        {
            result = await CreateFido2(origin).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertionResponse,
                OriginalOptions = originalOptions,
                StoredPublicKey = stored.PublicKey,
                StoredSignatureCounter = stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                    Task.FromResult(args.UserHandle.SequenceEqual(userId.ToByteArray()))
            });
        }
        catch (Fido2VerificationException)
        {
            return false;
        }

        // Persist the advanced counter. Fido2NetLib already rejects a counter that goes
        // backwards (the clone signal); storing the new value keeps that check meaningful
        // for the next assertion.
        await _store.UpdateCredentials(userId, document =>
        {
            UserPasskey? target = document.Passkeys.FirstOrDefault(p => p.CredentialId == credentialId);

            if (target is null)
                return;

            target.SignCount = result.SignCount;
            target.IsBackedUp = result.IsBackedUp;
            target.LastUsedOn = DateTimeOffset.UtcNow;
        });

        return true;
    }

    public async Task RemovePasskey(Guid userId, string credentialId)
        => await _store.UpdateCredentials(userId, document => document.Passkeys.RemoveAll(p => p.CredentialId == credentialId));

    public async Task RenamePasskey(Guid userId, string credentialId, string name)
        => await _store.UpdateCredentials(userId, document =>
        {
            UserPasskey? target = document.Passkeys.FirstOrDefault(p => p.CredentialId == credentialId);

            if (target is not null)
                target.Name = string.IsNullOrWhiteSpace(name) ? "Passkey" : name.Trim();
        });

    internal static PasskeySummary ToSummary(UserPasskey passkey) => new()
    {
        CredentialId = passkey.CredentialId,
        Name = passkey.Name,
        CreatedOn = passkey.CreatedOn,
        LastUsedOn = passkey.LastUsedOn,
        IsBackedUp = passkey.IsBackedUp
    };

    private static string GetAccountName(UserModel user)
        => (user.PrimaryEmailAddress ?? user.EmailAddresses.FirstOrDefault())?.Input
            ?? (user.PrimaryPhoneNumber ?? user.PhoneNumbers.FirstOrDefault())?.Input
            ?? user.ID.ToString();
}
