using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin.Native;

/// <summary>What the backend handed back to a native application on its callback URL.</summary>
public sealed record CloudLoginNativeCallback(string? Handoff, string? State, string? Operation, string? Error)
{
    public bool IsLogout => string.Equals(Operation, CloudLoginNativeProtocol.LogoutOperation, StringComparison.Ordinal);
}

/// <summary>The parts of the native sign-in that do not depend on a platform: the URLs, the callback, and the checks on it.</summary>
public static class CloudLoginNativeProtocol
{
    public const string LoginPath = "auth/native/login";
    public const string ExchangePath = "auth/native/exchange";
    public const string SessionEndPath = "auth/native/logout";
    public const string TokenPath = "auth/token";
    public const string LogoutOperation = "logout";

    private const int MinStateLength = 16;
    private const int MaxStateLength = 256;

    public static string CallbackUrl(string scheme, string host = "auth", string path = "/callback")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return $"{scheme}://{host}{(path.StartsWith('/') ? path : "/" + path)}";
    }

    public static string LoginUrl(string applicationUrl, string verifier, string state, string callbackUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);

        if (!IsValidState(state))
            throw new ArgumentException("A native state is 16 to 256 unreserved characters.", nameof(state));

        return $"{applicationUrl.TrimEnd('/')}/{LoginPath}"
            + $"?challenge={Uri.EscapeDataString(CloudLoginPkce.CreateChallenge(verifier))}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}";
    }

    /// <summary>
    /// Reads a callback URL. Only a URL on the expected callback (same scheme, host and path) is read, so a URL meant for something else
    /// is never mistaken for the end of a sign-in.
    /// </summary>
    public static bool TryParseCallback(string? url, string expectedCallbackUrl, out CloudLoginNativeCallback callback)
    {
        callback = new CloudLoginNativeCallback(null, null, null, null);

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? received) ||
            !Uri.TryCreate(expectedCallbackUrl, UriKind.Absolute, out Uri? expected))
            return false;

        if (!string.Equals(received.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(received.Host, expected.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(received.AbsolutePath.TrimEnd('/'), expected.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal))
            return false;

        Dictionary<string, string> query = ParseQuery(received.Query);
        callback = new CloudLoginNativeCallback(Value(query, "handoff"), Value(query, "state"), Value(query, "operation"), Value(query, "error"));

        return callback.Handoff is not null || callback.Error is not null || callback.IsLogout;
    }

    public static bool IsValidState(string? state) =>
        state is { Length: >= MinStateLength and <= MaxStateLength } && state.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~');

    public static bool StateMatches(string? received, string? expected) =>
        !string.IsNullOrEmpty(received) && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));

    private static string? Value(Dictionary<string, string> query, string name) =>
        query.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value) ? value : null;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string name = Uri.UnescapeDataString((separator < 0 ? pair : pair[..separator]).Replace('+', ' '));
            string value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));

            values.TryAdd(name, value);
        }

        return values;
    }
}
