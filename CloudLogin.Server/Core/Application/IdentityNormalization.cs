using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// The single place identities are normalized before hashing, lookup, or storage. Every path —
/// registration, sign-in, linking, migration — must produce byte-identical canonical strings for
/// the same identity, or the identity index falls apart.
/// </summary>
public sealed class IdentityNormalization(CloudGeographyClient cloudGeography)
{
    private readonly CloudGeographyClient _cloudGeography = cloudGeography;

    /// <summary>Lowercased, trimmed email address.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Canonical phone form: <c>+{callingCode}{nationalNumber}</c> when the calling code is
    /// resolvable, otherwise the digits of the input. Deterministic for any spelling of the same
    /// number that CloudGeography parses to the same result.
    /// </summary>
    public string NormalizePhone(string phoneInput)
    {
        PhoneNumber phoneNumber = _cloudGeography.PhoneNumbers.Get(phoneInput.Trim());
        return NormalizePhone(phoneNumber);
    }

    public static string NormalizePhone(PhoneNumber phoneNumber)
    {
        string digits = new([.. phoneNumber.Number.Where(char.IsDigit)]);
        string? callingCode = phoneNumber.CountryCallingCode?.TrimStart('+', '0');

        return string.IsNullOrEmpty(callingCode) ? digits : $"+{callingCode}{digits}";
    }

    /// <summary>Normalizes a contact by format name ("EmailAddress"/"PhoneNumber").</summary>
    public string NormalizeContact(string format, string value) => format switch
    {
        "EmailAddress" => NormalizeEmail(value),
        "PhoneNumber" => NormalizePhone(value),
        _ => value.Trim().ToLowerInvariant()
    };
}
