using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin.Server;

/// <summary>
/// RFC 6238 time-based one-time passwords, using the parameters every mainstream
/// authenticator app assumes: HMAC-SHA1, 6 digits, 30-second steps.
/// </summary>
public static class TotpAuthenticator
{
    private const int DigitCount = 6;
    private const int StepSeconds = 30;

    /// <summary>
    /// How many steps either side of "now" are accepted, covering clock drift between the
    /// server and the user's phone. One step each way is the usual compromise: it tolerates
    /// ±30s of skew without meaningfully widening the guessing window.
    /// </summary>
    private const int DriftSteps = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Creates a new 160-bit secret, Base32-encoded for authenticator apps.</summary>
    public static string CreateSecret()
    {
        byte[] key = RandomNumberGenerator.GetBytes(20);
        return ToBase32(key);
    }

    /// <summary>
    /// Builds the <c>otpauth://</c> URI an authenticator app consumes, normally via QR code.
    /// </summary>
    public static string BuildProvisioningUri(string secret, string accountName, string issuer)
    {
        string encodedIssuer = Uri.EscapeDataString(issuer);
        string encodedAccount = Uri.EscapeDataString(accountName);

        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}"
             + $"?secret={secret}"
             + $"&issuer={encodedIssuer}"
             + $"&algorithm=SHA1&digits={DigitCount}&period={StepSeconds}";
    }

    /// <summary>Validates a user-supplied code against the secret, allowing for clock drift.</summary>
    public static bool VerifyCode(string secret, string? code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;

        string normalized = code.Trim().Replace(" ", string.Empty);

        if (normalized.Length != DigitCount || !normalized.All(char.IsAsciiDigit))
            return false;

        byte[] key;

        try
        {
            key = FromBase32(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        long currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

        for (long step = currentStep - DriftSteps; step <= currentStep + DriftSteps; step++)
        {
            string candidate = ComputeCode(key, step);

            // Fixed-time comparison: a timing-distinguishable check here would leak how much
            // of a guessed code was correct.
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(candidate),
                    Encoding.ASCII.GetBytes(normalized)))
                return true;
        }

        return false;
    }

    private static string ComputeCode(byte[] key, long step)
    {
        byte[] counter = BitConverter.GetBytes(step);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);

        byte[] hash = HMACSHA1.HashData(key, counter);

        // RFC 4226 dynamic truncation.
        int offset = hash[^1] & 0x0F;

        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        return (binary % (int)Math.Pow(10, DigitCount)).ToString().PadLeft(DigitCount, '0');
    }

    private static string ToBase32(byte[] data)
    {
        StringBuilder builder = new();
        int buffer = 0;
        int bitsRemaining = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsRemaining += 8;

            while (bitsRemaining >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bitsRemaining - 5)) & 31]);
                bitsRemaining -= 5;
            }
        }

        if (bitsRemaining > 0)
            builder.Append(Base32Alphabet[(buffer << (5 - bitsRemaining)) & 31]);

        return builder.ToString();
    }

    private static byte[] FromBase32(string value)
    {
        string normalized = value.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", string.Empty);

        List<byte> output = [];
        int buffer = 0;
        int bitsRemaining = 0;

        foreach (char c in normalized)
        {
            int index = Base32Alphabet.IndexOf(c);

            if (index < 0)
                throw new FormatException($"'{c}' is not a valid Base32 character.");

            buffer = (buffer << 5) | index;
            bitsRemaining += 5;

            if (bitsRemaining >= 8)
            {
                output.Add((byte)((buffer >> (bitsRemaining - 8)) & 0xFF));
                bitsRemaining -= 8;
            }
        }

        return [.. output];
    }
}
