using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin;

public static class CloudLoginPkce
{
    public const string Method = "S256";

    public static string CreateVerifier() => Encode(RandomNumberGenerator.GetBytes(32));

    public static string CreateState() => Encode(RandomNumberGenerator.GetBytes(32));

    public static string CreateChallenge(string verifier) => Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>The sign-in address for a website without a backend or a native app. Its identity is the origin of <paramref name="redirectUri"/>.</summary>
    public static string BuildAuthorizeUrl(string authority, string redirectUri, string state, string verifier) =>
        $"{authority.TrimEnd('/')}/CloudLogin/Authorize" +
        $"?redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}" +
        $"&code_challenge={CreateChallenge(verifier)}&code_challenge_method={Method}";

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
