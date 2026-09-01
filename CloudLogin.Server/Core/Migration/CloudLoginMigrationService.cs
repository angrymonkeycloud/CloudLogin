using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Server.Core.Migration;

/// <summary>
/// Migrates the legacy mixed Cosmos container and per-user blob credential documents into the
/// seven-container core model.
/// <para>
/// Idempotent and checkpointed: user documents upsert, identity keys are create-only (a replay
/// of the same user is a no-op; a genuine collision is a review item, never a
/// first-match-wins pick), and the checkpoint records completed users so a crashed run restarts
/// where it stopped. Dry run reads and validates everything and writes nothing. The legacy
/// container is never written, deleted, or altered — it stays read-only for the deployment's
/// rollback window, and re-running the migration after a short write freeze is the catch-up
/// mechanism for changes that landed mid-run.
/// </para>
/// </summary>
public sealed class CloudLoginMigrationService(
    ILegacyUserSource legacySource,
    IMigrationCheckpointStore checkpoints,
    IUserRepository users,
    ICredentialRepository credentials,
    IIdentityKeyStore identityKeys,
    IdentityNormalization normalization,
    CloudLoginCoreConfiguration configuration,
    CloudLoginSecurityStore? blobSecurity = null,
    IDataProtectionProvider? dataProtection = null)
{
    private readonly ILegacyUserSource _legacySource = legacySource;
    private readonly IMigrationCheckpointStore _checkpoints = checkpoints;
    private readonly IUserRepository _users = users;
    private readonly ICredentialRepository _credentials = credentials;
    private readonly IIdentityKeyStore _identityKeys = identityKeys;
    private readonly IdentityNormalization _normalization = normalization;
    private readonly CloudLoginCoreConfiguration _configuration = configuration;
    private readonly CloudLoginSecurityStore? _blobSecurity = blobSecurity;
    private readonly IDataProtector? _totpProtector = dataProtection?.CreateProtector("CloudLogin.Totp.v1");

    public async Task<MigrationReport> RunAsync(MigrationOptions options, CancellationToken cancellationToken = default)
    {
        MigrationReport report = new()
        {
            DryRun = options.DryRun,
            StartedOn = DateTimeOffset.UtcNow
        };

        MigrationCheckpoint checkpoint = (options.ResumeFromCheckpoint && !options.DryRun
            ? await _checkpoints.LoadAsync(cancellationToken)
            : null) ?? new MigrationCheckpoint { StartedOn = DateTimeOffset.UtcNow };

        int sinceCheckpointSave = 0;
        Dictionary<string, Guid> runIdentityOwners = new(StringComparer.Ordinal);

        await foreach (CloudUser legacyUser in _legacySource.EnumerateUsersAsync(cancellationToken))
        {
            report.UsersRead++;

            if (legacyUser.ID == Guid.Empty || legacyUser.Inputs.Count == 0)
            {
                report.UsersMalformed++;
                report.Issues.Add(new MigrationIssue
                {
                    Category = "malformed-user",
                    Severity = MigrationIssueSeverities.Error,
                    UserId = legacyUser.ID == Guid.Empty ? null : legacyUser.ID,
                    Message = legacyUser.ID == Guid.Empty
                        ? "User document has no id; cannot migrate."
                        : "User has no inputs (no email or phone identity); needs manual review."
                });
                continue;
            }

            string sourceFingerprint = Fingerprint(legacyUser);
            if (checkpoint.ProcessedFingerprints.TryGetValue(legacyUser.ID, out string? previousFingerprint) &&
                string.Equals(previousFingerprint, sourceFingerprint, StringComparison.Ordinal))
            {
                report.UsersSkippedAlreadyProcessed++;
                continue;
            }

            await MigrateUserAsync(
                legacyUser, options, report, runIdentityOwners, cancellationToken);

            checkpoint.ProcessedUserIds.Add(legacyUser.ID);
            checkpoint.ProcessedFingerprints[legacyUser.ID] = sourceFingerprint;
            checkpoint.UpdatedOn = DateTimeOffset.UtcNow;

            if (!options.DryRun && ++sinceCheckpointSave >= options.BatchSize)
            {
                await _checkpoints.SaveAsync(checkpoint, cancellationToken);
                sinceCheckpointSave = 0;
            }
        }

        if (!options.DryRun)
            await _checkpoints.SaveAsync(checkpoint, cancellationToken);

        // Consistency: legacy count vs what the core now holds.
        report.LegacyUserCount = await _legacySource.CountUsersAsync(cancellationToken);
        report.CoreUserCount = options.DryRun ? report.UsersMigrated : await _users.CountAsync(cancellationToken);
        report.CompletedOn = DateTimeOffset.UtcNow;

        await _checkpoints.SaveReportAsync(report, cancellationToken);
        return report;
    }

    private async Task MigrateUserAsync(
        CloudUser legacyUser,
        MigrationOptions options,
        MigrationReport report,
        Dictionary<string, Guid> runIdentityOwners,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // The user document is built first because everything else keys off its contact ids. On a
        // restart the ids already written are reused, so a re-run cannot mint fresh ids and orphan
        // the credentials and identity rows pointing at the old ones.
        UserDocument? existingUser = await _users.GetAsync(legacyUser.ID, cancellationToken);
        UserDocument user = BuildUserDocument(legacyUser, now, existingUser?.Contacts ?? []);

        // ── 1. Identity keys: create-only claims; collisions become review items ──

        List<IdentityReservation> identities = [];

        foreach (CloudLoginInput input in legacyUser.Inputs)
        {
            UserContact? contact = ContactFor(user, input);

            if (contact is null)
            {
                report.Issues.Add(new MigrationIssue
                {
                    Category = "malformed-identity",
                    Severity = MigrationIssueSeverities.Review,
                    UserId = legacyUser.ID,
                    // The address itself is an identity-hash input and never enters a report or
                    // a log line; the format and the contact id are enough to find it.
                    Message = $"A {input.Format} input could not be normalized and was skipped."
                });
                continue;
            }

            identities.Add(new IdentityReservation
            {
                Type = input.Format == CloudLoginInputFormat.PhoneNumber ? IdentityKeyTypes.Phone : IdentityKeyTypes.Email,
                CanonicalValue = input.Format == CloudLoginInputFormat.PhoneNumber
                    ? IdentityKey.CanonicalPhone(contact.NormalizedValue)
                    : IdentityKey.CanonicalEmail(contact.NormalizedValue),
                ContactId = contact.ContactId,
                // Legacy inputs only ever existed behind a completed verification flow.
                IsVerified = true
            });
        }

        // ── 2. Provider identifiers: convert only when the issuer is known safely ──

        List<CredentialDocument> newCredentials = [];

        foreach (CloudLoginInput input in legacyUser.Inputs)
        {
            UserContact? contact = ContactFor(user, input);

            if (contact is null)
                continue; // Already reported above.

            foreach (CloudLoginProvider provider in input.Providers)
            {
                if (!string.IsNullOrEmpty(provider.PasswordHash))
                {
                    // Hashes are preserved verbatim; existing passwords keep working.
                    newCredentials.Add(new CredentialDocument
                    {
                        Id = CredentialDocument.PasswordId(contact.ContactId),
                        UserId = legacyUser.ID.ToString(),
                        Kind = CredentialKinds.Password,
                        ContactId = contact.ContactId,
                        PasswordHash = provider.PasswordHash,
                        CreatedOn = legacyUser.CreatedOn,
                        UpdatedOn = now
                    });
                    report.PasswordCredentialsCreated++;
                }

                if (string.IsNullOrEmpty(provider.Identifier))
                    continue;

                if (KnownProviderIssuers.TryGet(provider.Code, out string issuer))
                {
                    newCredentials.Add(new CredentialDocument
                    {
                        Id = CredentialDocument.ExternalIdentityId(issuer, provider.Identifier),
                        UserId = legacyUser.ID.ToString(),
                        Kind = CredentialKinds.ExternalIdentity,
                        Issuer = issuer,
                        Subject = provider.Identifier,
                        ProviderCode = provider.Code,
                        LinkedContactId = contact.ContactId,
                        ProviderEmail = input.Format == CloudLoginInputFormat.EmailAddress ? contact.NormalizedValue : null,
                        ProviderEmailIsVerified = contact.IsVerified,
                        CreatedOn = legacyUser.CreatedOn,
                        UpdatedOn = now
                    });

                    identities.Add(new IdentityReservation
                    {
                        Type = IdentityKeyTypes.External,
                        CanonicalValue = IdentityKey.CanonicalExternal(issuer, provider.Identifier),
                        ContactId = contact.ContactId,
                        IsVerified = true
                    });
                    report.ProviderIdentifiersConverted++;
                }
                else
                {
                    report.ProviderIdentifiersFlagged++;
                    report.Issues.Add(new MigrationIssue
                    {
                        Category = "provider-identifier-unconverted",
                        Severity = MigrationIssueSeverities.Review,
                        UserId = legacyUser.ID,
                        Message = $"Provider '{provider.Code}' has identifier material but no safely known issuer; not converted."
                    });
                }
            }
        }

        // ── 3. Blob security documents: passkeys and the authenticator app ──

        if (options.IncludeBlobCredentials && _blobSecurity is not null)
        {
            try
            {
                CloudLoginUserSecurityDocument security = await _blobSecurity.GetCredentials(legacyUser.ID);

                foreach (CloudLoginPasskey passkey in security.Passkeys)
                {
                    newCredentials.Add(new CredentialDocument
                    {
                        Id = CredentialDocument.PasskeyId(passkey.CredentialId),
                        UserId = legacyUser.ID.ToString(),
                        Kind = CredentialKinds.Passkey,
                        PasskeyCredentialId = passkey.CredentialId,
                        PasskeyPublicKey = Convert.ToBase64String(passkey.PublicKey),
                        PasskeySignCount = passkey.SignCount,
                        PasskeyName = passkey.Name,
                        PasskeyAaGuid = passkey.AaGuid.ToString(),
                        PasskeyTransports = [.. passkey.Transports],
                        PasskeyIsBackedUp = passkey.IsBackedUp,
                        PasskeyLastUsedOn = passkey.LastUsedOn,
                        CreatedOn = passkey.CreatedOn,
                        UpdatedOn = now
                    });
                    report.PasskeysMigrated++;
                }

                if (security.Authenticator is { } authenticator && !string.IsNullOrEmpty(authenticator.SecretKey))
                {
                    if (_totpProtector is null)
                        report.Issues.Add(new MigrationIssue
                        {
                            Category = "totp-unprotected",
                            Severity = MigrationIssueSeverities.Error,
                            UserId = legacyUser.ID,
                            Message = "Authenticator secret found but no Data Protection provider is available to wrap it."
                        });
                    else
                    {
                        newCredentials.Add(new CredentialDocument
                        {
                            Id = CredentialDocument.TotpId,
                            UserId = legacyUser.ID.ToString(),
                            Kind = CredentialKinds.Totp,
                            ProtectedTotpSecret = _totpProtector.Protect(authenticator.SecretKey),
                            TotpIsConfirmed = authenticator.IsConfirmed,
                            TotpEnrolledOn = authenticator.EnrolledOn,
                            CreatedOn = authenticator.EnrolledOn,
                            UpdatedOn = now
                        });
                        report.AuthenticatorsMigrated++;
                    }
                }
            }
            catch (Exception exception)
            {
                report.Issues.Add(new MigrationIssue
                {
                    Category = "blob-credentials-unreadable",
                    Severity = MigrationIssueSeverities.Review,
                    UserId = legacyUser.ID,
                    Message = $"Blob security document could not be read: {exception.Message}"
                });
            }
        }

        // ── 4. Decide which identity claims this user may take ──

        bool identityConflict = false;
        List<IdentityReservation> claimableIdentities = [];

        foreach (IdentityReservation reservation in identities.DistinctBy(identity => identity.CanonicalValue))
        {
            Guid? conflictingUserId = runIdentityOwners.TryGetValue(reservation.CanonicalValue, out Guid runOwner)
                ? runOwner
                : (await _identityKeys.ResolveAsync(
                    _configuration.RealmId, reservation.CanonicalValue, cancellationToken))?.UserId;

            if (conflictingUserId is Guid conflict && conflict != legacyUser.ID)
            {
                identityConflict = true;
                report.DuplicateIdentities++;
                report.Issues.Add(new MigrationIssue
                {
                    Category = "duplicate-identity",
                    Severity = MigrationIssueSeverities.Review,
                    UserId = legacyUser.ID,
                    ConflictingUserId = conflict,
                    // The canonical value is an identity-hash input, so it stays out of the
                    // report: the type plus both user ids is enough to resolve the conflict.
                    Message = $"A {reservation.Type} identity on user {legacyUser.ID} is also present on user {conflict}."
                });
                continue;
            }

            runIdentityOwners[reservation.CanonicalValue] = legacyUser.ID;
            claimableIdentities.Add(reservation);
        }

        if (identityConflict)
            report.Issues.Add(new MigrationIssue
            {
                Category = "user-partially-claimable",
                Severity = MigrationIssueSeverities.Review,
                UserId = legacyUser.ID,
                Message = "User migrated, but one or more identities are claimed by another user (see duplicate-identity issues)."
            });

        // ── 5. Writes (skipped in dry run) ──

        if (options.DryRun)
        {
            report.UsersMigrated++;
            return;
        }

        foreach (IdentityReservation reservation in claimableIdentities)
        {
            try
            {
                await _identityKeys.InsertAsync(_configuration.RealmId, new IdentityKeyClaim
                {
                    Type = reservation.Type,
                    CanonicalValue = reservation.CanonicalValue,
                    UserId = legacyUser.ID,
                    ContactId = reservation.ContactId
                }, cancellationToken);

                report.IdentityKeysCreated++;
            }
            catch (CoreConflictException)
            {
                // Same-user replay (idempotent restart) or a race with another claim; verify.
                IdentityKey? holder = await _identityKeys.ResolveAsync(_configuration.RealmId, reservation.CanonicalValue, cancellationToken);

                if (holder is not null && holder.UserId != legacyUser.ID)
                {
                    report.DuplicateIdentities++;
                    report.Issues.Add(new MigrationIssue
                    {
                        Category = "duplicate-identity",
                        Severity = MigrationIssueSeverities.Review,
                        UserId = legacyUser.ID,
                        ConflictingUserId = holder.UserId,
                        Message = $"Identity claim lost to user {holder.UserId} during migration."
                    });
                }
            }
        }

        // Upsert semantics for the user document keep restarts idempotent while preserving ids.
        // Re-read rather than reuse the copy taken for the contact ids: writing on an ETag that
        // predates the claim loop would lose to any concurrent change instead of overwriting it.
        existingUser = await _users.GetAsync(legacyUser.ID, cancellationToken);

        if (existingUser is null)
            await _users.CreateAsync(user, cancellationToken);
        else
        {
            user.ETag = existingUser.ETag;
            await _users.ReplaceAsync(user, cancellationToken);
        }

        foreach (CredentialDocument credential in newCredentials)
            await _credentials.UpsertAsync(credential, cancellationToken);

        report.UsersMigrated++;
    }

    private UserDocument BuildUserDocument(CloudUser legacyUser, DateTimeOffset now, List<UserContact> existingContacts)
    {
        UserDocument user = new()
        {
            Id = legacyUser.ID.ToString(),
            FirstName = legacyUser.FirstName,
            LastName = legacyUser.LastName,
            DisplayName = legacyUser.DisplayName,
            Username = legacyUser.Username,
            DateOfBirth = legacyUser.DateOfBirth,
            IsLocked = legacyUser.IsLocked,
            IsTest = legacyUser.IsTest,
            IsGlobalAdmin = legacyUser.IsGlobalAdmin,
            CreatedOn = legacyUser.CreatedOn,
            UpdatedOn = now,
            LastSignedInOn = legacyUser.LastSignedIn,
            ProfilePicture = legacyUser.ProfilePicture,
            IsCustomProfilePicture = legacyUser.IsCustomProfilePicture,
            ProviderProfilePicture = legacyUser.ProviderProfilePicture,
            Country = legacyUser.Country,
            Locale = legacyUser.Locale
        };

        foreach (CloudLoginInput input in legacyUser.Inputs)
        {
            string? normalized = TryNormalize(input);

            if (normalized is null)
                continue; // Reported by the caller, which skips this input everywhere.

            user.Contacts.Add(new UserContact
            {
                // Reuse the id an earlier run already wrote for this address; mint one only for
                // an address the migrated document has never carried.
                ContactId = existingContacts
                    .FirstOrDefault(contact => string.Equals(contact.NormalizedValue, normalized, StringComparison.Ordinal))
                    ?.ContactId ?? Guid.NewGuid(),
                Format = input.Format.ToString(),
                Value = input.Input,
                NormalizedValue = normalized,
                IsPrimary = input.IsPrimary,
                IsVerified = true,
                PhoneNumberCountryCode = input.PhoneNumberCountryCode,
                PhoneNumberCallingCode = input.PhoneNumberCallingCode,
                ProviderCodes = [.. input.Providers.Select(provider => provider.Code).Distinct(StringComparer.OrdinalIgnoreCase)]
            });
        }

        return user;
    }

    /// <summary>The contact a legacy input became, or null when the input could not be normalized.</summary>
    private UserContact? ContactFor(UserDocument user, CloudLoginInput input)
    {
        string? normalized = TryNormalize(input);

        return normalized is null
            ? null
            : user.Contacts.FirstOrDefault(contact =>
                string.Equals(contact.NormalizedValue, normalized, StringComparison.Ordinal));
    }

    private string? TryNormalize(CloudLoginInput input)
    {
        try
        {
            return _normalization.NormalizeContact(input.Format.ToString(), input.Input);
        }
        catch
        {
            return null;
        }
    }

    private static string Fingerprint(CloudUser user)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(user, CloudLoginSerialization.Options);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }
}
