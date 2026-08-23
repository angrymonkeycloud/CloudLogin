using System.Text.Json;
using Fido2NetLib;

namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Self-service security operations for the signed-in user: password, authenticator app,
/// passkeys, linked providers, and sign-in history.
/// <para>
/// Every method here resolves the acting user through <see cref="CloudLoginServer.CurrentUser"/>
/// and operates only on that account. No method accepts a caller-supplied user id, so one user
/// can never read or mutate another's security state through this surface.
/// </para>
/// </summary>
public partial class CloudLoginServer
{
    private CloudLoginSecurityStore? _securityStore;
    private CloudLoginWebAuthnService? _webAuthnService;

    /// <summary>
    /// Fido2NetLib's types carry their own <c>[JsonPropertyName]</c> attributes (including the
    /// Base64Url-aware byte[] converters WebAuthn payloads need), so this only needs to relax
    /// case sensitivity — enough to tolerate a client that doesn't reproduce .NET's exact
    /// casing (e.g. "clientDataJson" vs the wire name "clientDataJSON") without silently
    /// dropping a required field.
    /// </summary>
    private static readonly JsonSerializerOptions WebAuthnJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Security state lives in blob storage, so these features require it to be configured.
    /// Built lazily rather than injected, to keep the public constructor signature unchanged
    /// for hosts that don't use them.
    /// </summary>
    private CloudLoginSecurityStore SecurityStore => _securityStore ??= _configuration.AzureStorage is null
        ? throw new InvalidOperationException("Azure Storage must be configured to use CloudLogin security features.")
        : new CloudLoginSecurityStore(_configuration.AzureStorage, _configuration.Security);

    private CloudLoginWebAuthnService WebAuthn => _webAuthnService ??=
        new CloudLoginWebAuthnService(SecurityStore, _configuration.Security);

    private async Task<CloudUser> RequireCurrentUser()
        => await CurrentUser() ?? throw new UnauthorizedAccessException("No signed-in user.");

    private static bool IsPasswordProvider(CloudLoginProvider provider)
        => provider.Code.Equals("password", StringComparison.OrdinalIgnoreCase);

    // ── Overview ─────────────────────────────────────────────────────────────

    /// <summary>Display-safe summary of the signed-in user's security posture.</summary>
    public async Task<CloudLoginSecurityOverview> GetSecurityOverview()
    {
        CloudUser user = await RequireCurrentUser();

        CloudLoginUserSecurityDocument credentials = _configuration.AzureStorage is null
            ? new CloudLoginUserSecurityDocument { UserId = user.ID }
            : await SecurityStore.GetCredentials(user.ID);

        List<CloudLoginProviderDefinition> configured = await GetProviders();

        // A provider is "connected" once it appears on any of the user's inputs.
        List<CloudLoginConnectedProvider> connected = [.. user.Inputs
            .SelectMany(input => input.Providers.Select(provider => new { input, provider }))
            .Select(pair => new CloudLoginConnectedProvider
            {
                Code = pair.provider.Code,
                Label = configured.FirstOrDefault(p => p.Code.Equals(pair.provider.Code, StringComparison.OrdinalIgnoreCase))?.Label
                        ?? pair.provider.Code,
                Input = pair.input.Input
            })];

        // Disconnecting the last remaining sign-in method would lock the user out, so that
        // case is reported as not-disconnectable rather than being silently allowed.
        bool moreThanOneMethod = connected.Count > 1;

        foreach (CloudLoginConnectedProvider provider in connected)
            provider.CanDisconnect = moreThanOneMethod;

        bool hasPassword = user.Inputs.SelectMany(i => i.Providers).Any(p => IsPasswordProvider(p) && !string.IsNullOrEmpty(p.PasswordHash));

        return new CloudLoginSecurityOverview
        {
            HasPassword = hasPassword,
            PasswordProviderConfigured = configured.Any(p => p.Code.Equals("password", StringComparison.OrdinalIgnoreCase)),
            HasAuthenticatorApp = credentials.Authenticator is { IsConfirmed: true },
            AuthenticatorEnrolledOn = credentials.Authenticator is { IsConfirmed: true } ? credentials.Authenticator.EnrolledOn : null,
            Passkeys = [.. credentials.Passkeys
                .OrderByDescending(p => p.CreatedOn)
                .Select(CloudLoginWebAuthnService.ToSummary)],
            ConnectedProviders = connected,
            AvailableProviders = [.. configured.Where(definition =>
                definition.IsExternal &&
                !connected.Any(c => c.Code.Equals(definition.Code, StringComparison.OrdinalIgnoreCase)))]
        };
    }

    // ── Login history ────────────────────────────────────────────────────────

    /// <summary>Sign-in history for the signed-in user, newest first.</summary>
    public async Task<List<CloudLoginHistoryEntry>> GetMyLoginHistory()
    {
        CloudUser user = await RequireCurrentUser();

        if (_configuration.AzureStorage is null)
            return [];

        return await SecurityStore.GetLoginHistory(user.ID);
    }

    /// <summary>
    /// Records a completed sign-in. Failures are swallowed: an audit write must never be able
    /// to fail an otherwise valid authentication.
    /// </summary>
    public async Task RecordSignInForUser(Guid userId, CloudLoginHistoryEntry entry)
    {
        if (_configuration.AzureStorage is null)
            return;

        try
        {
            await SecurityStore.RecordSignIn(userId, entry);
        }
        catch
        {
            // Intentionally ignored — see summary.
        }
    }

    // ── Password ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets or changes the signed-in user's password. When one already exists the current
    /// password must be supplied and verified first, so a hijacked session can't silently
    /// take over the account.
    /// </summary>
    public async Task ChangeMyPassword(CloudLoginChangePasswordRequest request)
    {
        CloudUser user = await RequireCurrentUser();

        if (!IsValidPassword(request.NewPassword))
            throw new ArgumentException($"Password must be at least {_configuration.Security.MinimumPasswordLength} characters and not a commonly used password.");

        CloudLoginProvider? existing = user.Inputs
            .SelectMany(input => input.Providers)
            .FirstOrDefault(provider => IsPasswordProvider(provider) && !string.IsNullOrEmpty(provider.PasswordHash));

        if (existing is not null)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword))
                throw new ArgumentException("Your current password is required to change it.");

            if (!VerifyPassword(request.CurrentPassword, existing.PasswordHash!, out _))
                throw new ArgumentException("Your current password is incorrect.");
        }

        string hashed = await HashPassword(request.NewPassword);

        if (existing is not null)
        {
            // Keep every password provider entry in step, so the credential is consistent
            // regardless of which input the user signs in through.
            foreach (CloudLoginProvider provider in user.Inputs.SelectMany(i => i.Providers).Where(IsPasswordProvider))
                provider.PasswordHash = hashed;
        }
        else
        {
            CloudLoginInput target = user.PrimaryEmailAddress
                ?? user.EmailAddresses.FirstOrDefault()
                ?? user.Inputs.FirstOrDefault()
                ?? throw new InvalidOperationException("This account has no input to attach a password to.");

            target.Providers.Add(new CloudLoginProvider { Code = "Password", PasswordHash = hashed });
        }

        await UpdateUser(user);
    }

    // ── Linked providers ─────────────────────────────────────────────────────

    /// <summary>
    /// Unlinks a provider from the signed-in user's account. Refuses when it is the only
    /// remaining sign-in method.
    /// </summary>
    public async Task DisconnectProvider(string providerCode, string input)
    {
        CloudUser user = await RequireCurrentUser();

        int totalMethods = user.Inputs.Sum(i => i.Providers.Count);

        if (totalMethods <= 1)
            throw new InvalidOperationException("You can't remove your only sign-in method.");

        CloudLoginInput? targetInput = user.Inputs
            .FirstOrDefault(i => i.Input.Equals(input, StringComparison.OrdinalIgnoreCase));

        CloudLoginProvider? provider = targetInput?.Providers
            .FirstOrDefault(p => p.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase));

        if (targetInput is null || provider is null)
            throw new InvalidOperationException("That provider isn't linked to your account.");

        targetInput.Providers.Remove(provider);

        await UpdateUser(user);
    }

    // ── Authenticator app (TOTP) ─────────────────────────────────────────────

    /// <summary>
    /// Starts authenticator enrollment, returning the shared secret and its otpauth URI.
    /// The enrollment stays unconfirmed — and therefore inactive — until the user proves
    /// possession via <see cref="ConfirmAuthenticatorEnrollment"/>.
    /// </summary>
    public async Task<CloudLoginAuthenticatorEnrollment> BeginAuthenticatorEnrollment()
    {
        CloudUser user = await RequireCurrentUser();

        string secret = TotpAuthenticator.CreateSecret();

        await SecurityStore.UpdateCredentials(user.ID, document => document.Authenticator = new CloudLoginAuthenticatorApp
        {
            SecretKey = secret,
            EnrolledOn = DateTimeOffset.UtcNow,
            IsConfirmed = false
        });

        string accountName = (user.PrimaryEmailAddress ?? user.EmailAddresses.FirstOrDefault())?.Input
            ?? user.DisplayName
            ?? user.ID.ToString();

        return new CloudLoginAuthenticatorEnrollment
        {
            SecretKey = secret,
            ProvisioningUri = TotpAuthenticator.BuildProvisioningUri(secret, accountName, _configuration.Security.WebAuthnRelyingPartyName)
        };
    }

    /// <summary>Confirms enrollment by validating a code the authenticator app produced.</summary>
    public async Task<bool> ConfirmAuthenticatorEnrollment(string code)
    {
        CloudUser user = await RequireCurrentUser();

        CloudLoginUserSecurityDocument credentials = await SecurityStore.GetCredentials(user.ID);

        if (credentials.Authenticator is null)
            return false;

        if (!TotpAuthenticator.VerifyCode(credentials.Authenticator.SecretKey, code))
            return false;

        await SecurityStore.UpdateCredentials(user.ID, document =>
        {
            if (document.Authenticator is not null)
                document.Authenticator.IsConfirmed = true;
        });

        return true;
    }

    /// <summary>Removes the authenticator enrollment entirely.</summary>
    public async Task DisableAuthenticator()
    {
        CloudUser user = await RequireCurrentUser();
        await SecurityStore.UpdateCredentials(user.ID, document => document.Authenticator = null);
    }

    /// <summary>Validates a TOTP code against the signed-in user's confirmed enrollment.</summary>
    public async Task<bool> VerifyAuthenticatorCode(string code)
    {
        CloudUser user = await RequireCurrentUser();

        CloudLoginUserSecurityDocument credentials = await SecurityStore.GetCredentials(user.ID);

        return credentials.Authenticator is { IsConfirmed: true }
            && TotpAuthenticator.VerifyCode(credentials.Authenticator.SecretKey, code);
    }

    // ── Passkeys (WebAuthn) ──────────────────────────────────────────────────

    // WebAuthn ceremonies cross the wire as JSON rather than as Fido2NetLib types. The browser
    // needs that JSON verbatim for navigator.credentials anyway, and keeping the library types
    // server-side means neither the shared contracts nor the client take a Fido2 dependency.

    /// <summary>Creation options for <c>navigator.credentials.create()</c>, as JSON.</summary>
    public async Task<string> BeginPasskeyRegistration()
    {
        CloudUser user = await RequireCurrentUser();
        CredentialCreateOptions options = await WebAuthn.BeginRegistration(user, RequestOrigin());

        return JsonSerializer.Serialize(options, WebAuthnJsonOptions);
    }

    /// <summary>Verifies an attestation response and stores the credential.</summary>
    public async Task<CloudLoginPasskeySummary> CompletePasskeyRegistration(string optionsJson, string attestationJson, string? name)
    {
        CloudUser user = await RequireCurrentUser();

        CredentialCreateOptions options = JsonSerializer.Deserialize<CredentialCreateOptions>(optionsJson, WebAuthnJsonOptions)
            ?? throw new ArgumentException("Invalid registration options.", nameof(optionsJson));

        AuthenticatorAttestationRawResponse attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson, WebAuthnJsonOptions)
            ?? throw new ArgumentException("Invalid attestation response.", nameof(attestationJson));

        return await WebAuthn.CompleteRegistration(user, RequestOrigin(), options, attestation, name);
    }

    /// <summary>Request options for <c>navigator.credentials.get()</c>, as JSON.</summary>
    public async Task<string> BeginPasskeyAssertion()
    {
        CloudUser user = await RequireCurrentUser();
        AssertionOptions options = await WebAuthn.BeginAssertion(user.ID, RequestOrigin());

        return JsonSerializer.Serialize(options, WebAuthnJsonOptions);
    }

    /// <summary>Verifies an assertion, e.g. when confirming a sensitive change.</summary>
    public async Task<bool> CompletePasskeyAssertion(string optionsJson, string assertionJson)
    {
        CloudUser user = await RequireCurrentUser();

        AssertionOptions options = JsonSerializer.Deserialize<AssertionOptions>(optionsJson, WebAuthnJsonOptions)
            ?? throw new ArgumentException("Invalid assertion options.", nameof(optionsJson));

        AuthenticatorAssertionRawResponse assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson, WebAuthnJsonOptions)
            ?? throw new ArgumentException("Invalid assertion response.", nameof(assertionJson));

        return await WebAuthn.CompleteAssertion(user.ID, RequestOrigin(), options, assertion);
    }

    public async Task RemovePasskey(string credentialId)
    {
        CloudUser user = await RequireCurrentUser();
        await WebAuthn.RemovePasskey(user.ID, credentialId);
    }

    public async Task RenamePasskey(string credentialId, string name)
    {
        CloudUser user = await RequireCurrentUser();
        await WebAuthn.RenamePasskey(user.ID, credentialId, name);
    }

    /// <summary>
    /// The origin WebAuthn ceremonies are bound to. Taken from the live request rather than
    /// configuration so the value always matches what the browser actually saw.
    /// </summary>
    private Uri RequestOrigin() => new($"{_request.Scheme}://{_request.Host}");
}
