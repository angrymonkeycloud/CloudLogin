using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin;

public class CloudLoginProviderDefinition
{
    public CloudLoginProviderDefinition(string code, bool handleUpdateOnly = false, string? label = null)
    {
        Code = code;
        Label = label ?? Code;
        HandleUpdateOnly = handleUpdateOnly;
    }

    public string Code { get; init; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HandleUpdateOnly { get; set; }

    public required bool HandlesEmailAddress { get; set; }
    public required bool HandlesPhoneNumber { get; set; }
    public required bool IsCodeVerification { get; set; }
    public required bool InputRequired { get; set; }
    public required bool IsExternal { get; set; }

    /// <summary>
    /// Whether CloudLogin checks a credential of its own for this provider, rather than handing
    /// sign-in to somebody else or waving it through.
    /// </summary>
    /// <remarks>
    /// True for the password provider and for every code-verification provider (email code,
    /// WhatsApp). False for external providers, which prove identity elsewhere, and false for test
    /// mode, which proves nothing. What it decides is whether an account has credentials worth
    /// managing at all: an authority that only federates has no password to change and nothing for
    /// a second factor to protect.
    /// </remarks>
    [JsonIgnore]
    public bool VerifiesCredentials =>
        !IsExternal && (IsCodeVerification || Code.Equals("password", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string CssClass // Should Be private
    {
        get
        {
            List<string> classes = [$"_{Code.ToLowerInvariant()}"];

            return string.Join(" ", classes);
        }
    }
}
