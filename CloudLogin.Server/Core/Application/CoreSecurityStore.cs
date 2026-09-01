using System.Globalization;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public sealed class CoreSecurityStore(
    ICredentialRepository credentials,
    IAuditEventRepository auditEvents,
    IUserRepository users,
    IDataProtectionProvider dataProtection,
    CloudLoginCoreConfiguration configuration) : ICloudLoginSecurityStore
{
    private readonly IDataProtector _totp =
        dataProtection.CreateProtector("AngryMonkey.CloudLogin.Totp.v1");

    public async Task<CloudLoginUserSecurityDocument> GetCredentials(Guid userId)
    {
        List<CredentialDocument> documents = await credentials.GetAllForUserAsync(userId);
        CloudLoginUserSecurityDocument result = new() { UserId = userId };

        foreach (CredentialDocument document in documents)
        {
            if (document.Kind == CredentialKinds.Passkey &&
                document.PasskeyCredentialId is not null &&
                document.PasskeyPublicKey is not null)
            {
                result.Passkeys.Add(new CloudLoginPasskey
                {
                    CredentialId = document.PasskeyCredentialId,
                    PublicKey = Convert.FromBase64String(document.PasskeyPublicKey),
                    SignCount = document.PasskeySignCount ?? 0,
                    Name = document.PasskeyName,
                    AaGuid = Guid.TryParse(document.PasskeyAaGuid, out Guid aaGuid) ? aaGuid : Guid.Empty,
                    Transports = document.PasskeyTransports ?? [],
                    IsBackedUp = document.PasskeyIsBackedUp ?? false,
                    CreatedOn = document.CreatedOn,
                    LastUsedOn = document.PasskeyLastUsedOn
                });
            }
            else if (document.Kind == CredentialKinds.Totp &&
                     document.ProtectedTotpSecret is not null)
            {
                result.Authenticator = new CloudLoginAuthenticatorApp
                {
                    SecretKey = _totp.Unprotect(document.ProtectedTotpSecret),
                    EnrolledOn = document.TotpEnrolledOn ?? document.CreatedOn,
                    IsConfirmed = document.TotpIsConfirmed ?? false
                };
            }
        }

        return result;
    }

    public async Task UpdateCredentials(Guid userId, Action<CloudLoginUserSecurityDocument> mutate)
    {
        CloudLoginUserSecurityDocument document = await GetCredentials(userId);
        mutate(document);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<CredentialDocument> existing = await credentials.GetAllForUserAsync(userId);

        HashSet<string> retainedPasskeys = document.Passkeys
            .Select(passkey => CredentialDocument.PasskeyId(passkey.CredentialId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (CredentialDocument removed in existing.Where(candidate =>
                     candidate.Kind == CredentialKinds.Passkey && !retainedPasskeys.Contains(candidate.Id)))
            await credentials.DeleteAsync(userId, removed.Id);

        foreach (CloudLoginPasskey passkey in document.Passkeys)
        {
            string id = CredentialDocument.PasskeyId(passkey.CredentialId);
            CredentialDocument? current = existing.FirstOrDefault(candidate => candidate.Id == id);
            await credentials.UpsertAsync(new CredentialDocument
            {
                Id = id,
                UserId = userId.ToString(),
                Kind = CredentialKinds.Passkey,
                PasskeyCredentialId = passkey.CredentialId,
                PasskeyPublicKey = Convert.ToBase64String(passkey.PublicKey),
                PasskeySignCount = passkey.SignCount,
                PasskeyName = passkey.Name,
                PasskeyAaGuid = passkey.AaGuid.ToString(),
                PasskeyTransports = [.. passkey.Transports],
                PasskeyIsBackedUp = passkey.IsBackedUp,
                PasskeyLastUsedOn = passkey.LastUsedOn,
                CreatedOn = current?.CreatedOn ?? passkey.CreatedOn,
                UpdatedOn = now
            });
        }

        if (document.Authenticator is null)
            await credentials.DeleteAsync(userId, CredentialDocument.TotpId);
        else
        {
            CredentialDocument? current = existing.FirstOrDefault(candidate =>
                candidate.Id == CredentialDocument.TotpId);
            await credentials.UpsertAsync(new CredentialDocument
            {
                Id = CredentialDocument.TotpId,
                UserId = userId.ToString(),
                Kind = CredentialKinds.Totp,
                ProtectedTotpSecret = _totp.Protect(document.Authenticator.SecretKey),
                TotpIsConfirmed = document.Authenticator.IsConfirmed,
                TotpEnrolledOn = document.Authenticator.EnrolledOn,
                CreatedOn = current?.CreatedOn ?? now,
                UpdatedOn = now
            });
        }

        await RotateSecurityStampAsync(userId);
    }

    public Task DeleteCredentials(Guid userId) => credentials.DeleteAllForUserAsync(userId);

    public async Task RecordSignIn(Guid userId, CloudLoginHistoryEntry entry)
    {
        AuditEventDocument audit = new()
        {
            Id = entry.ID.ToString(),
            Realm = configuration.RealmId,
            UserId = userId.ToString(),
            EventType = "Login.Succeeded",
            OccurredOn = entry.SignedInOn,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            PartitionKey = AuditEventDocument.BuildPartitionKey(
                configuration.RealmId, userId.ToString(), entry.SignedInOn),
            Data = new Dictionary<string, string>
            {
                ["Provider"] = entry.Provider ?? string.Empty,
                ["Device"] = entry.Device ?? string.Empty,
                ["Latitude"] = entry.Latitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["Longitude"] = entry.Longitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            },
            ExpiresOn = entry.SignedInOn + configuration.AuditRetention
        };
        DocumentExpiry.Recompute(audit);
        await auditEvents.AppendAsync(audit);
    }

    public async Task<List<CloudLoginHistoryEntry>> GetLoginHistory(Guid userId)
    {
        List<AuditEventDocument> events = [];
        DateTimeOffset cursor = DateTimeOffset.UtcNow;
        DateTimeOffset oldest = cursor - configuration.AuditRetention;

        while (cursor >= oldest)
        {
            string partition = AuditEventDocument.BuildPartitionKey(
                configuration.RealmId, userId.ToString(), cursor);
            events.AddRange(await auditEvents.GetPartitionAsync(partition, 100));
            cursor = new DateTimeOffset(cursor.Year, cursor.Month, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMonths(-1);
        }

        return [.. events
            .Where(item => item.EventType == "Login.Succeeded" && !DocumentExpiry.IsExpired(item))
            .OrderByDescending(item => item.OccurredOn)
            .Take(100)
            .Select(item => new CloudLoginHistoryEntry
            {
                ID = Guid.TryParse(item.Id, out Guid id) ? id : Guid.NewGuid(),
                SignedInOn = item.OccurredOn,
                Provider = Value(item, "Provider"),
                Device = Value(item, "Device"),
                IpAddress = item.IpAddress,
                UserAgent = item.UserAgent,
                Latitude = Number(item, "Latitude"),
                Longitude = Number(item, "Longitude")
            })];
    }

    public Task DeleteLoginHistory(Guid userId) => Task.CompletedTask;

    private async Task RotateSecurityStampAsync(Guid userId)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            UserDocument? user = await users.GetAsync(userId);
            if (user is null)
                return;

            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.UpdatedOn = DateTimeOffset.UtcNow;

            try
            {
                await users.ReplaceAsync(user);
                return;
            }
            catch (CoreConcurrencyException) when (attempt == 0)
            {
            }
        }
    }

    private static string? Value(AuditEventDocument item, string key) =>
        item.Data?.TryGetValue(key, out string? value) == true && value.Length > 0 ? value : null;

    private static double? Number(AuditEventDocument item, string key) =>
        double.TryParse(Value(item, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value : null;
}
