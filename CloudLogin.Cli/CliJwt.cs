using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Cli;

/// <summary>Reads a token's claims for display. It does not validate anything; the services that receive the token do.</summary>
internal static class CliJwt
{
    private static readonly string[] Shown = ["iss", "aud", "sub", "sid", "act", "scope", "iat", "exp"];

    public static IReadOnlyDictionary<string, string> ReadClaims(string token)
    {
        Dictionary<string, string> claims = [];
        string[] parts = token.Split('.');

        if (parts.Length != 3)
            return claims;

        try
        {
            using JsonDocument payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));

            foreach (string name in Shown)
                if (payload.RootElement.TryGetProperty(name, out JsonElement value))
                    claims[name] = Format(name, value);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
        }

        return claims;
    }

    private static string Format(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when name is "iat" or "exp" && value.TryGetInt64(out long seconds) =>
            DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("u", System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.String => value.GetString() ?? string.Empty,
        _ => value.GetRawText()
    };

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }
}
