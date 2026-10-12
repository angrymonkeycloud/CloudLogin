using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Cli;

public sealed record CliCookie(string Name, string Value, string Domain, string Path, DateTimeOffset? ExpiresOn, bool Secure, bool HttpOnly);

public sealed record CliUser(Guid Id, string? DisplayName, string? Email);

/// <summary>
/// A signed-in session as the native application would hold it: the backend's cookie, which is the session, and the system browser's
/// cookies, which hold the sign-in at the authority.
/// </summary>
public sealed record CliSession(
    string ApplicationUrl,
    string Scheme,
    CliUser? User,
    IReadOnlyList<CliCookie> ApplicationCookies,
    IReadOnlyList<CliCookie> BrowserCookies,
    DateTimeOffset SignedInOn)
{
    public static IReadOnlyList<CliCookie> Capture(CookieContainer container) =>
        [.. container.GetAllCookies()
            .Where(cookie => !cookie.Expired)
            .Select(cookie => new CliCookie(
                cookie.Name,
                cookie.Value,
                cookie.Domain,
                cookie.Path,
                cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()),
                cookie.Secure,
                cookie.HttpOnly))];

    public static CookieContainer Restore(IEnumerable<CliCookie> cookies)
    {
        CookieContainer container = new();

        foreach (CliCookie stored in cookies)
        {
            if (stored.ExpiresOn is { } expiresOn && expiresOn <= DateTimeOffset.UtcNow)
                continue;

            Cookie cookie = new(stored.Name, stored.Value, string.IsNullOrEmpty(stored.Path) ? "/" : stored.Path, stored.Domain)
            {
                Secure = stored.Secure,
                HttpOnly = stored.HttpOnly
            };

            if (stored.ExpiresOn is { } expiry)
                cookie.Expires = expiry.UtcDateTime;

            container.Add(cookie);
        }

        return container;
    }
}

public enum SessionLoadStatuses
{
    Loaded,
    Missing,
    Unreadable
}

public sealed record SessionLoadResult(SessionLoadStatuses Status, CliSession? Session = null);

public interface ISessionProtector
{
    string Description { get; }
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public static class SessionProtectors
{
    public static ISessionProtector ForCurrentPlatform() => OperatingSystem.IsWindows() ? new WindowsUserSessionProtector() : new FilePermissionSessionProtector();
}

/// <summary>Encrypts the session to the signed-in Windows user, so the file is useless to anyone else, even with a copy of it.</summary>
public sealed class WindowsUserSessionProtector : ISessionProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AngryMonkey.CloudLogin.Cli.Session.v1");

    public string Description => "encrypted for the current Windows user";

    public byte[] Protect(byte[] data) => OperatingSystem.IsWindows()
        ? ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser)
        : throw new PlatformNotSupportedException();

    public byte[] Unprotect(byte[] data) => OperatingSystem.IsWindows()
        ? ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser)
        : throw new PlatformNotSupportedException();
}

/// <summary>Keeps the session readable only by its owner through file permissions, where no per-user encryption is available.</summary>
public sealed class FilePermissionSessionProtector : ISessionProtector
{
    public string Description => "readable by the current user only";

    public byte[] Protect(byte[] data) => data;

    public byte[] Unprotect(byte[] data) => data;
}

public sealed class SessionStore(string path, ISessionProtector protector)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public string Description => protector.Description;

    public async Task<SessionLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
            return new SessionLoadResult(SessionLoadStatuses.Missing);

        try
        {
            byte[] stored = await File.ReadAllBytesAsync(Path, cancellationToken);
            CliSession? session = JsonSerializer.Deserialize<CliSession>(protector.Unprotect(stored), JsonOptions);

            return session is { ApplicationUrl.Length: > 0, Scheme.Length: > 0 }
                ? new SessionLoadResult(SessionLoadStatuses.Loaded, session)
                : new SessionLoadResult(SessionLoadStatuses.Unreadable);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return new SessionLoadResult(SessionLoadStatuses.Unreadable);
        }
    }

    public async Task SaveAsync(CliSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        string? directory = System.IO.Path.GetDirectoryName(Path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporary = Path + ".tmp";
        await File.WriteAllBytesAsync(temporary, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions)), cancellationToken);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temporary, Path, overwrite: true);
    }

    public bool Delete()
    {
        if (!File.Exists(Path))
            return false;

        File.Delete(Path);
        return true;
    }
}
