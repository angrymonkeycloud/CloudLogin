using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// Composes and decomposes the legacy <see cref="CloudUser"/> contract over the split core
/// storage: profile from the <c>Users</c> container, hashes and subjects from the
/// <c>Credentials</c> container, identity claims in the Table Storage index.
/// <para>
/// This is what lets every V2 code path keep its exact behavior while nothing sensitive lives in
/// the user document anymore. Materialized hashes and subjects exist only server-side inside the
/// composed object; the existing transport-security layer keeps stripping them before anything
/// leaves the process, and <see cref="SaveAsync"/> never deletes a credential just because a
/// round-tripped <see cref="CloudUser"/> arrived without hashes.
/// </para>
/// </summary>
public sealed class CoreUserService(
    IUserRepository users,
    ICredentialRepository credentials,
    IdentityLinkingService identityLinking,
    IdentityNormalization normalization)
{
    private readonly IUserRepository _users = users;
    private readonly ICredentialRepository _credentials = credentials;
    private readonly IdentityLinkingService _identityLinking = identityLinking;
    private readonly IdentityNormalization _normalization = normalization;

    // ── Composition (core documents → CloudUser) ─────────────────────────────

    /// <summary>Loads a fully materialized user, credentials included, for server-side logic.</summary>
    public async Task<CloudUser?> LoadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        UserDocument? user = await _users.GetAsync(userId, cancellationToken);
        if (user is null || user.State == UserStates.Deleted)
            return null;

        List<CredentialDocument> userCredentials = await _credentials.GetAllForUserAsync(userId, cancellationToken);
        return Compose(user, userCredentials);
    }

    /// <summary>Composes without loading credentials — for lists, where hashes are never needed.</summary>
    public static CloudUser ComposeProfileOnly(UserDocument user) => Compose(user, []);

    public static CloudUser Compose(UserDocument user, List<CredentialDocument> userCredentials)
    {
        CloudUser cloudUser = new()
        {
            ID = Guid.Parse(user.Id),
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            Username = user.Username,
            DateOfBirth = user.DateOfBirth,
            IsLocked = user.IsLocked || user.State == UserStates.Disabled,
            IsTest = user.IsTest,
            IsGlobalAdmin = user.IsGlobalAdmin,
            CreatedOn = user.CreatedOn,
            LastSignedIn = user.LastSignedInOn,
            ProfilePicture = user.ProfilePicture,
            IsCustomProfilePicture = user.IsCustomProfilePicture,
            ProviderProfilePicture = user.ProviderProfilePicture,
            Country = user.Country,
            Locale = user.Locale
        };

        List<CredentialDocument> passwords = [.. userCredentials.Where(credential => credential.Kind == CredentialKinds.Password)];
        List<CredentialDocument> externals = [.. userCredentials.Where(credential => credential.Kind == CredentialKinds.ExternalIdentity)];

        foreach (UserContact contact in user.Contacts)
        {
            CloudLoginInput input = new()
            {
                Input = contact.Value,
                Format = Enum.TryParse(contact.Format, out CloudLoginInputFormat format) ? format : CloudLoginInputFormat.Other,
                IsPrimary = contact.IsPrimary,
                PhoneNumberCountryCode = contact.PhoneNumberCountryCode,
                PhoneNumberCallingCode = contact.PhoneNumberCallingCode
            };

            foreach (string providerCode in contact.ProviderCodes)
            {
                CloudLoginProvider provider = new() { Code = providerCode };

                // Credentials are found by the contact's immutable id, never by its address: a
                // corrected or re-normalized address must still find its own password.
                if (string.Equals(providerCode, "Password", StringComparison.OrdinalIgnoreCase))
                    provider.PasswordHash = passwords.FirstOrDefault(credential =>
                        credential.ContactId == contact.ContactId)?.PasswordHash;
                else
                    provider.Identifier = externals.FirstOrDefault(credential =>
                        string.Equals(credential.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase) &&
                        (credential.LinkedContactId is null || credential.LinkedContactId == contact.ContactId))?.Subject;

                input.Providers.Add(provider);
            }

            cloudUser.Inputs.Add(input);
        }

        return cloudUser;
    }

    // ── Decomposition (CloudUser → core documents) ───────────────────────────

    /// <summary>
    /// Persists a materialized user back to the split model: profile to the user document,
    /// changed hashes and subjects to credential documents, contact changes to the identity
    /// index. Additive for credentials — removal always goes through the explicit unlink and
    /// disconnect paths, never through a profile save.
    /// </summary>
    public async Task SaveAsync(CloudUser cloudUser, bool isCreate, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        UserDocument? existing = isCreate ? null : await _users.GetAsync(cloudUser.ID, cancellationToken);
        UserDocument user = existing ?? new UserDocument { Id = cloudUser.ID.ToString(), CreatedOn = cloudUser.CreatedOn == DateTimeOffset.MinValue ? now : cloudUser.CreatedOn };

        user.FirstName = cloudUser.FirstName;
        user.LastName = cloudUser.LastName;
        user.DisplayName = cloudUser.DisplayName;
        user.Username = cloudUser.Username;
        user.DateOfBirth = cloudUser.DateOfBirth;
        user.IsLocked = cloudUser.IsLocked;
        user.IsTest = cloudUser.IsTest;
        user.IsGlobalAdmin = cloudUser.IsGlobalAdmin;
        user.LastSignedInOn = cloudUser.LastSignedIn;
        user.ProfilePicture = cloudUser.ProfilePicture;
        user.IsCustomProfilePicture = cloudUser.IsCustomProfilePicture;
        user.ProviderProfilePicture = cloudUser.ProviderProfilePicture;
        user.Country = cloudUser.Country;
        user.Locale = cloudUser.Locale;
        user.UpdatedOn = now;

        List<UserContact> previousContacts = existing?.Contacts ?? [];

        // A contact's id is assigned once and never reassigned, so a contact that survives the
        // save keeps the id its credentials and identity rows already point at. Matching on the
        // normalized value is only how the same contact is recognised across the round trip —
        // the id itself never derives from the address.
        user.Contacts = [.. cloudUser.Inputs.Select(input => ToContact(input, previousContacts))];

        // Contact changes drive the identity index.
        List<IdentityReservation> added = [];

        foreach (UserContact contact in user.Contacts)
        {
            if (previousContacts.Any(previous => previous.ContactId == contact.ContactId))
                continue;

            added.Add(ContactIdentity(contact));
        }

        List<UserContact> removed = [.. previousContacts.Where(previous =>
            !user.Contacts.Any(contact => contact.ContactId == previous.ContactId))];

        // Credentials that arrived materialized on the CloudUser (login paths set hashes;
        // provider callbacks set identifiers). Absence means "no change", never "delete".
        List<CredentialDocument> credentialUpserts = [];

        foreach (CloudLoginInput input in cloudUser.Inputs)
        {
            string normalizedValue = _normalization.NormalizeContact(input.Format.ToString(), input.Input);
            UserContact contact = user.Contacts.First(candidate =>
                string.Equals(candidate.NormalizedValue, normalizedValue, StringComparison.Ordinal));

            foreach (CloudLoginProvider provider in input.Providers)
            {
                if (string.Equals(provider.Code, "Password", StringComparison.OrdinalIgnoreCase) && provider.PasswordHash is not null)
                    credentialUpserts.Add(new CredentialDocument
                    {
                        Id = CredentialDocument.PasswordId(contact.ContactId),
                        UserId = user.Id,
                        Kind = CredentialKinds.Password,
                        ContactId = contact.ContactId,
                        PasswordHash = provider.PasswordHash,
                        CreatedOn = now,
                        UpdatedOn = now
                    });
                else if (provider.Identifier is not null)
                {
                    string issuer = KnownProviderIssuers.GetOrFallback(provider.Code);

                    credentialUpserts.Add(new CredentialDocument
                    {
                        Id = CredentialDocument.ExternalIdentityId(issuer, provider.Identifier),
                        UserId = user.Id,
                        Kind = CredentialKinds.ExternalIdentity,
                        Issuer = issuer,
                        Subject = provider.Identifier,
                        ProviderCode = provider.Code,
                        LinkedContactId = contact.ContactId,
                        ProviderEmail = contact.Format == nameof(CloudLoginInputFormat.EmailAddress) ? contact.NormalizedValue : null,
                        ProviderEmailIsVerified = contact.IsVerified,
                        CreatedOn = now,
                        UpdatedOn = now
                    });

                    added.Add(new IdentityReservation
                    {
                        Type = IdentityKeyTypes.External,
                        CanonicalValue = IdentityKey.CanonicalExternal(issuer, provider.Identifier),
                        ContactId = contact.ContactId,
                        // The provider flow completing is the verification of an external identity.
                        IsVerified = true
                    });
                }
            }
        }

        if (isCreate)
        {
            await _identityLinking.RegisterNewUserAsync(user, DistinctIdentities(added), credentialUpserts, cancellationToken);
            return;
        }

        List<string> newlyClaimed = [];
        try
        {
            foreach (IdentityReservation reservation in DistinctIdentities(added))
                if (await _identityLinking.ClaimIdentityAsync(reservation, cloudUser.ID, cancellationToken))
                    newlyClaimed.Add(reservation.CanonicalValue);

            await _users.ReplaceAsync(user, cancellationToken);
        }
        catch
        {
            foreach (string canonical in newlyClaimed)
            {
                try { await _identityLinking.ReleaseIdentityAsync(cloudUser.ID, canonical, cancellationToken); }
                catch { }
            }
            throw;
        }

        foreach (CredentialDocument credential in credentialUpserts)
            await _credentials.UpsertAsync(credential, cancellationToken);

        foreach (UserContact contact in removed)
        {
            await _identityLinking.ReleaseIdentityAsync(cloudUser.ID, ContactIdentity(contact).CanonicalValue, cancellationToken);
            await _credentials.DeleteAsync(cloudUser.ID, CredentialDocument.PasswordId(contact.ContactId), cancellationToken);
        }
    }

    /// <summary>Deletes the user and releases every identity, credential, and index entry.</summary>
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        UserDocument? user = await _users.GetAsync(userId, cancellationToken);

        if (user is not null)
        {
            foreach (UserContact contact in user.Contacts)
                await _identityLinking.ReleaseIdentityAsync(userId, ContactIdentity(contact).CanonicalValue, cancellationToken);

            List<CredentialDocument> externals = await _credentials.GetByKindAsync(userId, CredentialKinds.ExternalIdentity, cancellationToken);
            foreach (CredentialDocument external in externals.Where(credential => credential.Issuer is not null && credential.Subject is not null))
                await _identityLinking.ReleaseIdentityAsync(
                    userId, IdentityKey.CanonicalExternal(external.Issuer!, external.Subject!), cancellationToken);
        }

        await _credentials.DeleteAllForUserAsync(userId, cancellationToken);
        await _users.DeleteAsync(userId, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Projects a round-tripped input back onto a contact, reusing the id of the contact it
    /// already is. A new id is minted only for an address this account has never held.
    /// </summary>
    private UserContact ToContact(CloudLoginInput input, List<UserContact> previousContacts)
    {
        string normalizedValue = _normalization.NormalizeContact(input.Format.ToString(), input.Input);

        UserContact? previous = previousContacts.FirstOrDefault(candidate =>
            string.Equals(candidate.NormalizedValue, normalizedValue, StringComparison.Ordinal));

        return new UserContact
        {
            ContactId = previous?.ContactId ?? Guid.NewGuid(),
            Format = input.Format.ToString(),
            Value = input.Input,
            NormalizedValue = normalizedValue,
            IsPrimary = input.IsPrimary,
            IsVerified = true, // Legacy inputs were only ever added through verified flows.
            PhoneNumberCountryCode = input.PhoneNumberCountryCode,
            PhoneNumberCallingCode = input.PhoneNumberCallingCode,
            ProviderCodes = [.. input.Providers.Select(provider => provider.Code).Distinct(StringComparer.OrdinalIgnoreCase)]
        };
    }

    private static IdentityReservation ContactIdentity(UserContact contact) =>
        string.Equals(contact.Format, nameof(CloudLoginInputFormat.PhoneNumber), StringComparison.Ordinal)
            ? new IdentityReservation
            {
                Type = IdentityKeyTypes.Phone,
                CanonicalValue = IdentityKey.CanonicalPhone(contact.NormalizedValue),
                ContactId = contact.ContactId,
                IsVerified = contact.IsVerified
            }
            : new IdentityReservation
            {
                Type = IdentityKeyTypes.Email,
                CanonicalValue = IdentityKey.CanonicalEmail(contact.NormalizedValue),
                ContactId = contact.ContactId,
                IsVerified = contact.IsVerified
            };

    private static List<IdentityReservation> DistinctIdentities(List<IdentityReservation> identities) =>
        [.. identities.DistinctBy(identity => identity.CanonicalValue)];
}
