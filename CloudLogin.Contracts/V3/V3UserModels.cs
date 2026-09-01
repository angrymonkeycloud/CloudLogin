namespace AngryMonkey.CloudLogin.V3;

/// <summary>
/// V3 contract models. Every V3 request and response is an explicit DTO defined here — no
/// persistence document, no internal model, and nothing secret ever crosses this boundary:
/// no hashes, TOTP secrets, token values, provider subjects, signing material, ETags, or
/// storage partition keys.
/// </summary>
public sealed record V3ContactModel
{
    /// <summary>"EmailAddress" or "PhoneNumber".</summary>
    public required string Format { get; init; }

    public required string Value { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsVerified { get; init; }

    /// <summary>Provider codes usable with this contact, for display ("Password", "Google", ...).</summary>
    public List<string> Providers { get; init; } = [];
}

/// <summary>The signed-in user's own profile.</summary>
public sealed record V3SelfProfileResponse
{
    public required Guid UserId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? Username { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Country { get; init; }
    public string? Locale { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastSignedIn { get; init; }
    public List<V3ContactModel> Contacts { get; init; } = [];
}

/// <summary>What anyone (or any signed-in non-admin) may learn about another user: the minimum.</summary>
public sealed record V3PublicUserSummaryResponse
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? ProfilePictureUrl { get; init; }
}

/// <summary>The administrator's view. Authorized to global administrators only.</summary>
public sealed record V3AdminUserResponse
{
    public required Guid UserId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? Username { get; init; }
    public string? Country { get; init; }
    public string? Locale { get; init; }
    public bool IsLocked { get; init; }
    public bool IsTest { get; init; }
    public bool IsGlobalAdmin { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastSignedIn { get; init; }
    public List<V3ContactModel> Contacts { get; init; } = [];
}

/// <summary>The service-to-service view for trusted backends: identifiers and display data only.</summary>
public sealed record V3ServiceUserResponse
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? PrimaryEmail { get; init; }
    public string? Country { get; init; }
    public string? Locale { get; init; }
}

/// <summary>Login discovery: what the login page needs to route an identifier, and nothing else.</summary>
public sealed record V3LoginDiscoveryRequest
{
    public required string Identifier { get; init; }
}

public sealed record V3LoginDiscoveryResponse
{
    /// <summary>Whether an account exists for the identifier. Rate limited; no profile data rides along.</summary>
    public required bool AccountExists { get; init; }

    /// <summary>Provider codes the account can sign in with, filtered to the active sign-in profile.</summary>
    public List<string> AvailableMethods { get; init; } = [];
}

/// <summary>Self-service profile update. Only these fields; everything else is server-managed.</summary>
public sealed record V3UpdateProfileRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? Username { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Country { get; init; }
    public string? Locale { get; init; }
}
