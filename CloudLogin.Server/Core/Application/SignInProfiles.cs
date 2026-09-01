using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// A named sign-in experience selected by the login URL (<c>?profile=tv</c>). Profiles restrict
/// what an entry page displays and which authorization methods may complete the flow; they can
/// only narrow the deployment's configured providers, never enable anything new.
/// </summary>
public sealed class CloudLoginSignInProfile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Entry methods shown on the login page ("Password", "Code", "Google", "Qr", ...). Empty
    /// means every client-allowed method.
    /// </summary>
    public List<string> VisibleMethods { get; set; } = [];

    /// <summary>
    /// Methods permitted to actually authorize the sign-in. Empty means every client-allowed
    /// method. For a QR-only profile this is what the mobile approval page uses: the TV shows
    /// only the QR entry while approval happens through these methods.
    /// </summary>
    public List<string> AllowedMethods { get; set; } = [];
}

/// <summary>Deployment-wide sign-in profile configuration.</summary>
public sealed class SignInProfileConfiguration
{
    public const string DefaultProfileName = "default";

    /// <summary>The QR/device-only entry method name.</summary>
    public const string QrMethod = "Qr";

    public List<CloudLoginSignInProfile> Profiles { get; set; } =
    [
        new CloudLoginSignInProfile { Name = DefaultProfileName }
    ];

    /// <summary>The profile used when none is requested, or when a request fails safely.</summary>
    public string DefaultProfile { get; set; } = DefaultProfileName;

    /// <summary>
    /// Per-client profile allowances: client identifier (redirect origin or client id) to the
    /// profile names that client may request. The default profile needs no allowance; every
    /// non-default profile must be explicitly allowed here per client.
    /// </summary>
    public Dictionary<string, List<string>> ClientProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A resolved, tamper-evident profile selection.</summary>
public sealed record SignInProfileSelection
{
    public required CloudLoginSignInProfile Profile { get; init; }

    /// <summary>True when the requested name was unknown or unauthorized and the default was used instead.</summary>
    public required bool FellBackToDefault { get; init; }
}

/// <summary>
/// Resolves and binds sign-in profiles. Resolution fails safely — an unknown or unauthorized
/// profile silently becomes the configured default — and the selected name is bound into
/// Data Protection-sealed authorization state, so changing a URL parameter mid-flow cannot
/// change the profile that governs completion.
/// </summary>
public sealed class SignInProfileService(SignInProfileConfiguration configuration, IDataProtectionProvider dataProtection)
{
    private readonly SignInProfileConfiguration _configuration = configuration;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("CloudLogin.SignInProfile.v1");

    /// <summary>
    /// Resolves the requested profile for a client. The default profile is always available;
    /// any other profile requires the client's explicit allowance.
    /// </summary>
    public SignInProfileSelection Resolve(string? requestedProfile, string? clientId)
    {
        CloudLoginSignInProfile defaultProfile = FindProfile(_configuration.DefaultProfile)
            ?? new CloudLoginSignInProfile { Name = SignInProfileConfiguration.DefaultProfileName };

        if (string.IsNullOrWhiteSpace(requestedProfile) ||
            string.Equals(requestedProfile, defaultProfile.Name, StringComparison.OrdinalIgnoreCase))
            return new SignInProfileSelection { Profile = defaultProfile, FellBackToDefault = false };

        CloudLoginSignInProfile? profile = FindProfile(requestedProfile);
        if (profile is null)
            return new SignInProfileSelection { Profile = defaultProfile, FellBackToDefault = true };

        bool clientAllowed = clientId is not null
            && _configuration.ClientProfiles.TryGetValue(clientId, out List<string>? allowed)
            && allowed.Contains(profile.Name, StringComparer.OrdinalIgnoreCase);

        if (!clientAllowed)
            return new SignInProfileSelection { Profile = defaultProfile, FellBackToDefault = true };

        return new SignInProfileSelection { Profile = profile, FellBackToDefault = false };
    }

    /// <summary>
    /// Whether a method may complete authorization under the profile. An empty allowlist means
    /// the profile does not restrict methods (the client/provider configuration still applies).
    /// </summary>
    public static bool AllowsMethod(CloudLoginSignInProfile profile, string methodCode) =>
        profile.AllowedMethods.Count == 0
        || profile.AllowedMethods.Contains(methodCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>Seals the resolved profile (and the client it was resolved for) into protected state.</summary>
    public string Bind(SignInProfileSelection selection, string? clientId)
    {
        BoundProfileState state = new()
        {
            Profile = selection.Profile.Name,
            Client = clientId,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        return _protector.Protect(JsonSerializer.Serialize(state));
    }

    /// <summary>
    /// Unseals previously bound state. Returns null — never a fallback — when the payload was
    /// tampered with, so callers restart the flow instead of continuing with a guessed profile.
    /// </summary>
    public CloudLoginSignInProfile? Unbind(string boundState, string? expectedClient = null)
    {
        BoundProfileState? state;

        try
        {
            state = JsonSerializer.Deserialize<BoundProfileState>(_protector.Unprotect(boundState));
        }
        catch
        {
            return null;
        }

        if (state is null || state.ExpiresOn <= DateTimeOffset.UtcNow ||
            !string.Equals(state.Client, expectedClient, StringComparison.Ordinal))
            return null;

        return FindProfile(state.Profile);
    }

    private CloudLoginSignInProfile? FindProfile(string name) =>
        _configuration.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

    private sealed record BoundProfileState
    {
        public required string Profile { get; init; }
        public string? Client { get; init; }
        public DateTimeOffset ExpiresOn { get; init; }
    }
}
