namespace AngryMonkey.CloudLogin.V3;

// The CloudLogin admin portal's contract. Like the rest of V3 it is explicit DTOs only: enums travel as
// their names, and no hash, secret, signing material or storage detail is ever a member of a response.
// The one value that must reach an administrator once, a newly created secret key, is carried by
// V3AdminIssuedSecretKeyResponse.KeyValue and is never readable again.

/// <summary>What the signed-in administrator may do, so the portal can show only what will work.</summary>
public sealed record V3AdminMeResponse
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public bool IsGlobalAdmin { get; init; }

    /// <summary>Administrator role names, for example "ApplicationAdministrator".</summary>
    public List<string> Roles { get; init; } = [];

    /// <summary>Permission names, for example "applications.manage".</summary>
    public List<string> Permissions { get; init; } = [];
}

public sealed record V3AdminAuditEventModel
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset OccurredOn { get; init; }
    public string? Result { get; init; }
    public Guid? UserId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ClientId { get; init; }
    public string? IpAddress { get; init; }

    /// <summary>Small, non-sensitive details only.</summary>
    public Dictionary<string, string> Data { get; init; } = [];
}

public sealed record V3AdminAuditPageResponse
{
    public List<V3AdminAuditEventModel> Events { get; init; } = [];
}

public sealed record V3AdminDashboardResponse
{
    public int Users { get; init; }
    public int ActiveSessions { get; init; }
    public int Applications { get; init; }
    public int ActiveApplications { get; init; }
    public int BlockedApplications { get; init; }

    /// <summary>Applications that have not authenticated for a long time (90 days).</summary>
    public int DormantApplications { get; init; }

    /// <summary>Secret keys expired or expiring within 30 days.</summary>
    public int ExpiringKeys { get; init; }

    public int FailedAuthenticationsLast24Hours { get; init; }
    public List<V3AdminAuditEventModel> RecentEvents { get; init; } = [];
}

// ── Users ────────────────────────────────────────────────────────────────────────────────────

public sealed record V3AdminUserSummaryResponse
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public string? PrimaryContact { get; init; }

    /// <summary>Active, Disabled, PendingDeletion or Deleted.</summary>
    public required string State { get; init; }

    public bool IsLocked { get; init; }
    public bool IsGlobalAdmin { get; init; }
    public List<string> Roles { get; init; } = [];
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastSignedInOn { get; init; }
}

public sealed record V3AdminLinkedIdentityModel
{
    public required string CredentialId { get; init; }
    public string? ProviderCode { get; init; }
    public string? ProviderEmail { get; init; }
    public bool EmailIsVerified { get; init; }
    public DateTimeOffset LinkedOn { get; init; }
}

public sealed record V3AdminPasskeyModel
{
    public required string CredentialId { get; init; }
    public string? Name { get; init; }
    public bool? IsBackedUp { get; init; }
    public DateTimeOffset RegisteredOn { get; init; }
    public DateTimeOffset? LastUsedOn { get; init; }
}

public sealed record V3AdminUserDetailResponse
{
    public required V3AdminUserSummaryResponse Summary { get; init; }
    public string? Username { get; init; }
    public string? Country { get; init; }
    public string? Locale { get; init; }
    public List<V3ContactModel> Contacts { get; init; } = [];

    /// <summary>Whether the account can sign in with a local credential.</summary>
    public bool HasLocalCredential { get; init; }

    public bool HasAuthenticatorApp { get; init; }
    public List<V3AdminPasskeyModel> Passkeys { get; init; } = [];
    public List<V3AdminLinkedIdentityModel> LinkedIdentities { get; init; } = [];
}

public sealed record V3AdminSetDisabledRequest
{
    public bool Disabled { get; init; }
}

// ── Applications ─────────────────────────────────────────────────────────────────────────────

/// <summary>An application seen using CloudLogin. Nothing here is configured: it is what the authority observed.</summary>
public sealed record V3AdminApplicationResponse
{
    /// <summary>The name a website with a backend gives itself, or the origin of a website without one or of a native app.</summary>
    public required string ClientId { get; init; }

    /// <summary>Backend, Website or NativeApp.</summary>
    public string Kind { get; init; } = "Backend";

    public List<string> Origins { get; init; } = [];
    public string? BackChannelLogoutUri { get; init; }
    public DateTimeOffset FirstSeenOn { get; init; }
    public DateTimeOffset LastSeenOn { get; init; }

    /// <summary>The id of the secret key a backend last authenticated with.</summary>
    public string? LastKeyId { get; init; }

    public int ActiveSessions { get; init; }

    /// <summary>Not seen for a long time (90 days).</summary>
    public bool IsDormant { get; init; }

    public bool IsBlocked { get; init; }
    public DateTimeOffset? BlockedOn { get; init; }
    public string? BlockReason { get; init; }
}

public sealed record V3AdminBlockApplicationRequest
{
    public string? Reason { get; init; }
}

public sealed record V3AdminRevokeRequest
{
    public string? Reason { get; init; }
}

// ── Secret keys ──────────────────────────────────────────────────────────────────────────────

/// <summary>A secret key, never its secret. Any website with a backend may use any active key.</summary>
public sealed record V3AdminSecretKeyModel
{
    public required string Id { get; init; }
    public string Label { get; init; } = string.Empty;

    /// <summary>The first characters of the secret, to recognise it. Empty for a key the deployment declared.</summary>
    public string? Prefix { get; init; }

    /// <summary>Declared by the deployment (an AppHost); its secret lives in the deployment's configuration.</summary>
    public bool IsDeployment { get; init; }

    /// <summary>Active, Expired or Revoked.</summary>
    public string Status { get; init; } = "Active";

    public DateTimeOffset? CreatedOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public DateTimeOffset? LastUsedOn { get; init; }
    public string? LastUsedBy { get; init; }
    public DateTimeOffset? RevokedOn { get; init; }
    public string? RevocationReason { get; init; }
}

public sealed record V3AdminCreateSecretKeyRequest
{
    public string? Label { get; init; }

    /// <summary>When the key stops working. Leave empty for a key that does not expire.</summary>
    public DateTimeOffset? ExpiresOn { get; init; }
}

/// <summary>A key just created. <see cref="KeyValue"/> is shown this once and cannot be read again.</summary>
public sealed record V3AdminIssuedSecretKeyResponse
{
    public required V3AdminSecretKeyModel Key { get; init; }
    public required string KeyValue { get; init; }
}

// ── Sessions ─────────────────────────────────────────────────────────────────────────────────

public sealed record V3AdminSessionResponse
{
    public required string FamilyId { get; init; }
    public required string SessionId { get; init; }
    public required Guid UserId { get; init; }
    public string? UserName { get; init; }
    public string? Audience { get; init; }
    public string? ApplicationClientId { get; init; }
    public string? ApplicationName { get; init; }
    public string? Scope { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset? LastSeenOn { get; init; }
    public DateTimeOffset ExpiresOn { get; init; }
    public string? DeviceName { get; init; }
    public string? Browser { get; init; }
    public string? OperatingSystem { get; init; }
    public string? CreatedByIp { get; init; }
    public string? LastSeenIp { get; init; }
    public bool IsAuthoritySession { get; init; }
    public bool IsRevoked { get; init; }
    public string? RevocationReason { get; init; }
}

public sealed record V3AdminRevokedCountResponse
{
    public int Revoked { get; init; }
}

// ── Security: signing keys and providers ────────────────────────────────────────────────────

public sealed record V3AdminKeyModel
{
    public required string KeyId { get; init; }

    /// <summary>Active (signs), Retired (still verifies) or Expired.</summary>
    public required string Status { get; init; }

    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset SigningExpiresOn { get; init; }
    public DateTimeOffset PublishExpiresOn { get; init; }
    public bool ExpiresSoon { get; init; }
}

public sealed record V3AdminKeysResponse
{
    /// <summary>When true the keys live in Key Vault and are rotated there; none are listed here.</summary>
    public bool ManagedInKeyVault { get; init; }

    public List<V3AdminKeyModel> Keys { get; init; } = [];
}

public sealed record V3AdminProviderResponse
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public bool IsExternal { get; init; }
    public bool VerifiesCredentials { get; init; }
    public int LinkedIdentities { get; init; }
}

public sealed record V3AdminProviderUserModel
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? ProviderEmail { get; init; }
    public DateTimeOffset LinkedOn { get; init; }
}

// ── Administrators ──────────────────────────────────────────────────────────────────────────

public sealed record V3AdministratorResponse
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public string? PrimaryContact { get; init; }
    public bool IsGlobalAdmin { get; init; }
    public bool IsLocked { get; init; }
    public List<string> Roles { get; init; } = [];
}

public sealed record V3AdminSetRolesRequest
{
    public List<string> Roles { get; init; } = [];
}
