using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// Blob-backed persistence for per-user security state.
/// <para>
/// Two documents are kept per user, both under the configured storage container:
/// <c>security/{userId}/login-history.json</c> and <c>security/{userId}/credentials.json</c>.
/// They live outside the user record deliberately — sign-in history grows without bound on an
/// active account, and credential material (TOTP secrets, passkey public keys) must never ride
/// along with the <see cref="UserModel"/> that gets serialized to the browser.
/// </para>
/// </summary>
public sealed class CloudLoginSecurityStore(AzureStorageConfiguration storage, CloudLoginSecurityOptions security)
{
    private readonly AzureStorageConfiguration _storage = storage;
    private readonly CloudLoginSecurityOptions _security = security;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    /// <summary>Concurrent sign-ins race on the same blob; retry the read-modify-write a few times.</summary>
    private const int ConcurrencyRetries = 4;

    private static string HistoryPath(Guid userId) => $"security/{userId}/login-history.json";
    private static string CredentialsPath(Guid userId) => $"security/{userId}/credentials.json";

    private async Task<BlobContainerClient> GetContainerAsync()
    {
        BlobContainerClient container = new(_storage.ConnectionString, _storage.ContainerName);
        await container.CreateIfNotExistsAsync();
        return container;
    }

    private static async Task<(T? Document, ETag? ETag)> ReadAsync<T>(BlobClient blob) where T : class
    {
        try
        {
            Response<BlobDownloadResult> response = await blob.DownloadContentAsync();
            T? document = JsonSerializer.Deserialize<T>(response.Value.Content.ToString());
            return (document, response.Value.Details.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (null, null);
        }
    }

    private static async Task WriteAsync<T>(BlobClient blob, T document, ETag? etag)
    {
        BinaryData payload = new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, SerializerOptions)));

        // A null ETag means "must not already exist"; otherwise the write only lands if the
        // blob hasn't changed since it was read, so a parallel sign-in can't silently drop an entry.
        BlobUploadOptions options = new()
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Conditions = etag.HasValue
                ? new BlobRequestConditions { IfMatch = etag.Value }
                : new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        await blob.UploadAsync(payload, options);
    }

    // ── Login history ────────────────────────────────────────────────────────

    /// <summary>Returns the user's sign-in history, newest first, with retention already applied.</summary>
    public async Task<List<LoginHistoryEntry>> GetLoginHistory(Guid userId)
    {
        BlobContainerClient container = await GetContainerAsync();
        (LoginHistoryDocument? document, _) = await ReadAsync<LoginHistoryDocument>(container.GetBlobClient(HistoryPath(userId)));

        if (document is null)
            return [];

        return [.. Prune(document.Entries)];
    }

    /// <summary>
    /// Appends a sign-in record and prunes anything beyond the configured limits.
    /// Never throws into the sign-in path — a storage failure must not block a valid login.
    /// </summary>
    public async Task RecordSignIn(Guid userId, LoginHistoryEntry entry)
    {
        BlobClient blob = (await GetContainerAsync()).GetBlobClient(HistoryPath(userId));

        for (int attempt = 0; attempt <= ConcurrencyRetries; attempt++)
        {
            (LoginHistoryDocument? document, ETag? etag) = await ReadAsync<LoginHistoryDocument>(blob);

            document ??= new LoginHistoryDocument { UserId = userId };
            document.UserId = userId;
            document.Entries.Add(entry);
            document.Entries = [.. Prune(document.Entries)];

            try
            {
                await WriteAsync(blob, document, etag);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 409 && attempt < ConcurrencyRetries)
            {
                // Another sign-in wrote first — re-read and reapply on top of their version.
            }
        }
    }

    /// <summary>
    /// Applies both retention rules: drop anything older than the retention window, then keep
    /// only the newest N records. Ordering is newest-first so the account page can render directly.
    /// </summary>
    private IEnumerable<LoginHistoryEntry> Prune(IEnumerable<LoginHistoryEntry> entries)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - _security.LoginHistoryRetention;

        return entries
            .Where(entry => entry.SignedInOn >= cutoff)
            .OrderByDescending(entry => entry.SignedInOn)
            .Take(Math.Max(1, _security.LoginHistoryMaximumEntries));
    }

    /// <summary>Removes a user's history document entirely (account deletion).</summary>
    public async Task DeleteLoginHistory(Guid userId)
    {
        BlobContainerClient container = await GetContainerAsync();
        await container.GetBlobClient(HistoryPath(userId)).DeleteIfExistsAsync();
    }

    // ── Credentials (passkeys + authenticator app) ───────────────────────────

    public async Task<UserSecurityDocument> GetCredentials(Guid userId)
    {
        BlobContainerClient container = await GetContainerAsync();
        (UserSecurityDocument? document, _) = await ReadAsync<UserSecurityDocument>(container.GetBlobClient(CredentialsPath(userId)));

        return document ?? new UserSecurityDocument { UserId = userId };
    }

    /// <summary>
    /// Read-modify-write of the credential document under optimistic concurrency, so a passkey
    /// registration running alongside an authenticator enrollment can't overwrite the other.
    /// </summary>
    public async Task UpdateCredentials(Guid userId, Action<UserSecurityDocument> mutate)
    {
        BlobClient blob = (await GetContainerAsync()).GetBlobClient(CredentialsPath(userId));

        for (int attempt = 0; attempt <= ConcurrencyRetries; attempt++)
        {
            (UserSecurityDocument? document, ETag? etag) = await ReadAsync<UserSecurityDocument>(blob);

            document ??= new UserSecurityDocument { UserId = userId };
            document.UserId = userId;
            mutate(document);

            try
            {
                await WriteAsync(blob, document, etag);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 409 && attempt < ConcurrencyRetries)
            {
                // Lost the race; re-read and reapply.
            }
        }

        throw new InvalidOperationException("Could not save security credentials after repeated conflicts.");
    }

    public async Task DeleteCredentials(Guid userId)
    {
        BlobContainerClient container = await GetContainerAsync();
        await container.GetBlobClient(CredentialsPath(userId)).DeleteIfExistsAsync();
    }
}
