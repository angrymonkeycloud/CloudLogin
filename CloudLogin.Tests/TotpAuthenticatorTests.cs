using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Tests;

public class TotpAuthenticatorTests
{
    /// <summary>
    /// Independent RFC 6238 implementation used as an oracle. Deliberately written from the
    /// spec rather than reusing production code, so a bug present in both would have to be
    /// made twice to go unnoticed.
    /// </summary>
    private static string ReferenceCode(string base32Secret, long unixSeconds)
    {
        byte[] key = DecodeBase32(base32Secret);
        long step = unixSeconds / 30;

        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);

        byte[] hash = HMACSHA1.HashData(key, counter);
        int offset = hash[^1] & 0x0F;

        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString().PadLeft(6, '0');
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        List<byte> output = [];
        int buffer = 0, bits = 0;

        foreach (char c in value.Trim().TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }

    [Fact]
    public void CreateSecret_ProducesDecodableBase32OfExpectedLength()
    {
        string secret = TotpAuthenticator.CreateSecret();

        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));

        // 160 bits of entropy, per RFC 4226's recommendation.
        Assert.Equal(20, DecodeBase32(secret).Length);
    }

    [Fact]
    public void CreateSecret_IsDifferentEachTime()
    {
        HashSet<string> secrets = [.. Enumerable.Range(0, 25).Select(_ => TotpAuthenticator.CreateSecret())];

        Assert.Equal(25, secrets.Count);
    }

    [Fact]
    public void VerifyCode_AcceptsCodeForCurrentStep()
    {
        string secret = TotpAuthenticator.CreateSecret();
        string code = ReferenceCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.True(TotpAuthenticator.VerifyCode(secret, code));
    }

    [Theory]
    [InlineData(-30)]  // one step behind — a slightly slow phone
    [InlineData(30)]   // one step ahead — a slightly fast phone
    public void VerifyCode_AcceptsAdjacentStepsForClockDrift(int offsetSeconds)
    {
        string secret = TotpAuthenticator.CreateSecret();
        string code = ReferenceCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds);

        Assert.True(TotpAuthenticator.VerifyCode(secret, code));
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public void VerifyCode_RejectsCodesOutsideTheDriftWindow(int offsetSeconds)
    {
        string secret = TotpAuthenticator.CreateSecret();
        string code = ReferenceCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds);

        Assert.False(TotpAuthenticator.VerifyCode(secret, code));
    }

    [Fact]
    public void VerifyCode_RejectsCodeFromADifferentSecret()
    {
        string secret = TotpAuthenticator.CreateSecret();
        string otherSecret = TotpAuthenticator.CreateSecret();

        string code = ReferenceCode(otherSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.False(TotpAuthenticator.VerifyCode(secret, code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]      // too short
    [InlineData("1234567")]    // too long
    [InlineData("abcdef")]     // not digits
    [InlineData(null)]
    public void VerifyCode_RejectsMalformedInput(string? code)
    {
        string secret = TotpAuthenticator.CreateSecret();

        Assert.False(TotpAuthenticator.VerifyCode(secret, code));
    }

    [Fact]
    public void VerifyCode_ToleratesSpacingUsersCopyFromTheirApp()
    {
        string secret = TotpAuthenticator.CreateSecret();
        string code = ReferenceCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Authenticator apps display codes as "123 456"; pasting that shouldn't fail.
        string spaced = $"{code[..3]} {code[3..]}";

        Assert.True(TotpAuthenticator.VerifyCode(secret, spaced));
    }

    [Fact]
    public void VerifyCode_RejectsMalformedSecretWithoutThrowing()
    {
        // "1" and "8" are not in the Base32 alphabet.
        Assert.False(TotpAuthenticator.VerifyCode("not-valid-base32-118", "123456"));
    }

    [Fact]
    public void BuildProvisioningUri_ContainsTheParametersAuthenticatorAppsExpect()
    {
        string secret = TotpAuthenticator.CreateSecret();
        string uri = TotpAuthenticator.BuildProvisioningUri(secret, "person@example.com", "CloudLogin");

        Assert.StartsWith("otpauth://totp/CloudLogin:person%40example.com", uri);
        Assert.Contains($"secret={secret}", uri);
        Assert.Contains("issuer=CloudLogin", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void BuildProvisioningUri_EscapesAccountNamesContainingSeparators()
    {
        string uri = TotpAuthenticator.BuildProvisioningUri(TotpAuthenticator.CreateSecret(), "a:b c@example.com", "Angry Monkey");

        // A raw ':' or ' ' in the label would corrupt how the app parses issuer/account.
        Assert.Contains("Angry%20Monkey:a%3Ab%20c%40example.com", uri);
    }
}
