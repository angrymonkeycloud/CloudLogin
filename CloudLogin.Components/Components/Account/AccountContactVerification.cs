using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Models;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Tracks one in-flight "prove you own this contact" challenge for the account page.
/// <para>
/// Adding an email address or phone number from the account page goes through the same
/// ownership check as registration does: the address is claimed, a short numeric code is
/// sent to it, and the input is only written to the user record once that code comes back.
/// This mirrors the code/expiry handling in <c>LoginComponent</c> so both paths behave the
/// same way, rather than letting the account page attach unverified contacts.
/// </para>
/// </summary>
internal sealed class AccountContactVerification
{
    private const int CodeLength = 6;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    private string? _code;
    private DateTimeOffset _expiry;

    /// <summary>The contact currently awaiting verification, normalized and ready to persist.</summary>
    public string PendingInput { get; private set; } = string.Empty;

    /// <summary>True while a code has been sent and the user still has to enter it.</summary>
    public bool IsAwaitingCode => _code is not null;

    /// <summary>What the user has typed into the code box.</summary>
    public string EnteredCode { get; set; } = string.Empty;

    /// <summary>
    /// Starts a challenge for <paramref name="input"/> and returns the code to deliver.
    /// </summary>
    public string Start(string input)
    {
        PendingInput = input;
        EnteredCode = string.Empty;
        return Reissue();
    }

    /// <summary>Issues a fresh code for the same pending contact (the "resend" path).</summary>
    public string Reissue()
    {
        _code = CreateRandomCode(CodeLength);
        _expiry = DateTimeOffset.UtcNow.Add(CodeLifetime);
        return _code;
    }

    /// <summary>Validates what the user entered against the outstanding code.</summary>
    public VerificationCodeResult Check()
    {
        if (_code is null || !string.Equals(_code, EnteredCode?.Trim(), StringComparison.Ordinal))
            return VerificationCodeResult.NotValid;

        if (DateTimeOffset.UtcNow >= _expiry)
            return VerificationCodeResult.Expired;

        return VerificationCodeResult.Valid;
    }

    /// <summary>Clears the challenge, whether it succeeded or the user backed out.</summary>
    public void Reset()
    {
        _code = null;
        _expiry = default;
        PendingInput = string.Empty;
        EnteredCode = string.Empty;
    }

    private static string CreateRandomCode(int length)
    {
        StringBuilder builder = new();

        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] randomBytes = new byte[length];
        rng.GetBytes(randomBytes);

        for (int i = 0; i < length; i++)
            builder.Append(randomBytes[i] % 10);

        return builder.ToString();
    }
}
