using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Core.Domain;

/// <summary>How an application signs people in, as the authority last saw it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApplicationKinds
{
    /// <summary>A website with a backend: it proves itself with a secret key.</summary>
    Backend,

    /// <summary>A website without a backend. Its identity is its own origin; its tokens are only valid there.</summary>
    Website,

    /// <summary>A native app signing in directly, identified by its custom URL scheme.</summary>
    NativeApp
}

/// <summary>
/// An application that uses CloudLogin. Nobody creates these: one is recorded the first time it signs someone in, so the admin can see
/// what uses the authority and from where, and block one. The id is the name a backend gives itself, or a site's or app's own origin.
/// </summary>
public sealed class ApplicationDocument : CloudLoginCoreDocument
{
    public string ClientId { get; set; } = string.Empty;

    public ApplicationKinds Kind { get; set; } = ApplicationKinds.Backend;

    /// <summary>The origins it has been seen at, most recent first. A backend can run at several.</summary>
    public List<string> Origins { get; set; } = [];

    /// <summary>Where a backend asked to be told when a session ends. Announced by the backend itself, on its own origin.</summary>
    public string? BackChannelLogoutUri { get; set; }

    public DateTimeOffset FirstSeenOn { get; set; }
    public DateTimeOffset LastSeenOn { get; set; }

    /// <summary>The secret key a backend last authenticated with.</summary>
    public string? LastSecretKeyId { get; set; }

    public bool IsBlocked { get; set; }
    public DateTimeOffset? BlockedOn { get; set; }
    public string? BlockedByUserId { get; set; }
    public string? BlockReason { get; set; }
}

/// <summary>
/// A secret key any website with a backend can authenticate with. Keys are not tied to an application: several websites may share one,
/// and a website may switch keys. Only the hash is stored. A key declared by the deployment is listed with <see cref="IsDeployment"/> so
/// it can be revoked here too; its secret lives in the deployment's configuration.
/// </summary>
public sealed class SecretKeyDocument : CloudLoginCoreDocument
{
    public string Label { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    /// <summary>The first characters of the secret, so a key can be recognised without revealing it.</summary>
    public string? Prefix { get; set; }

    public bool IsDeployment { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
    public string? CreatedByUserId { get; set; }

    public DateTimeOffset? ExpiresOn { get; set; }

    public DateTimeOffset? LastUsedOn { get; set; }
    public string? LastUsedBy { get; set; }

    public DateTimeOffset? RevokedOn { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }

    public bool IsUsableAt(DateTimeOffset now) => RevokedOn is null && (ExpiresOn is not { } expires || expires > now);
}
