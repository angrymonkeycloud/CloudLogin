using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public enum OrganizationMembershipStates
{
    Invited,
    Active,
    Suspended,
    Removed
}

/// <summary>
/// Defaults for how many organizations a single user may own or belong to. A host overrides
/// them through <c>OrganizationConfiguration</c>; the values here apply when it leaves them unset.
/// </summary>
public static class OrganizationLimits
{
    /// <summary>Organizations one user may create when the host doesn't configure a cap.</summary>
    public const int DefaultMaxOwnedPerUser = 3;

    /// <summary>Organizations one user may belong to in total when the host doesn't configure a cap.</summary>
    public const int DefaultMaxPerUser = 10;

    /// <summary>Assign to a cap to remove it entirely.</summary>
    public const int Unlimited = int.MaxValue;
}

public sealed class OrganizationAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Line1)
        && string.IsNullOrWhiteSpace(Line2)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(State)
        && string.IsNullOrWhiteSpace(PostalCode)
        && string.IsNullOrWhiteSpace(Country);

    /// <summary>The populated parts, in postal order, joined for display on a single line.</summary>
    public override string ToString() => string.Join(", ", new[] { Line1, Line2, City, State, PostalCode, Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

public sealed class CloudLoginOrganization
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid OwnerUserId { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedOn { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    /// <summary>Registered legal entity name, when it differs from the display name.</summary>
    public string? LegalName { get; set; }

    /// <summary>Public website for this organization.</summary>
    public string? Website { get; set; }

    /// <summary>Contact phone number for this organization.</summary>
    public string? Phone { get; set; }

    /// <summary>Billing contact email for this organization. Editable by the organization owner.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>Billing contact name for this organization. Editable by the organization owner.</summary>
    public string? BillingContactName { get; set; }

    /// <summary>Tax or VAT registration number printed on this organization's invoices.</summary>
    public string? TaxId { get; set; }

    /// <summary>Postal address invoices are issued to.</summary>
    public OrganizationAddress BillingAddress { get; set; } = new();
}

public sealed class CloudLoginOrganizationMember
{
    public required Guid OrganizationId { get; init; }
    public required Guid UserId { get; init; }
    public OrganizationMembershipStates State { get; init; } = OrganizationMembershipStates.Active;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public bool IsOwner { get; init; }
    public DateTimeOffset JoinedOn { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CloudLoginOrganizationInvitation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid OrganizationId { get; init; }
    public required string Recipient { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTimeOffset ExpiresOn { get; init; }
    public DateTimeOffset CreatedOn { get; init; } = DateTimeOffset.UtcNow;
    public Guid InvitedByUserId { get; init; }

    public bool HasExpired => ExpiresOn <= DateTimeOffset.UtcNow;
}

/// <summary>
/// How much of a user's organization allowance is used. <see cref="Total"/> counts every
/// organization the user belongs to, owned ones included, so it is never below <see cref="Owned"/>.
/// </summary>
public sealed record OrganizationQuota
{
    public required int Owned { get; init; }
    public required int MaxOwned { get; init; }
    public required int Total { get; init; }
    public required int MaxTotal { get; init; }

    public bool OwnedIsUnlimited => MaxOwned >= OrganizationLimits.Unlimited;
    public bool TotalIsUnlimited => MaxTotal >= OrganizationLimits.Unlimited;

    public int RemainingOwned => OwnedIsUnlimited ? OrganizationLimits.Unlimited : Math.Max(0, MaxOwned - Owned);
    public int RemainingTotal => TotalIsUnlimited ? OrganizationLimits.Unlimited : Math.Max(0, MaxTotal - Total);

    /// <summary>A new organization counts against both caps, so both must have room.</summary>
    public bool CanCreate => RemainingOwned > 0 && RemainingTotal > 0;

    /// <summary>Joining someone else's organization counts only against the membership cap.</summary>
    public bool CanJoin => RemainingTotal > 0;
}

/// <summary>Which of a user's organization caps was reached.</summary>
public enum OrganizationLimitKinds
{
    /// <summary>The cap on organizations the user may create and own.</summary>
    Owned,

    /// <summary>The cap on organizations the user may belong to in total.</summary>
    Membership
}

/// <summary>Thrown when creating or joining an organization would exceed a configured cap.</summary>
public sealed class OrganizationLimitReachedException(OrganizationLimitKinds kind, int limit)
    : InvalidOperationException(kind == OrganizationLimitKinds.Owned
        ? $"This account has reached its limit of {limit} organization{(limit == 1 ? "" : "s")} it can create."
        : $"This account has reached its limit of {limit} organization{(limit == 1 ? "" : "s")} it can belong to.")
{
    public OrganizationLimitKinds Kind { get; } = kind;
    public int Limit { get; } = limit;
}

/// <summary>Reasons an organization can't be deleted yet.</summary>
[Flags]
public enum OrganizationDeletionBlockers
{
    None = 0,

    /// <summary>Subscriptions that are still running. They must expire or be cancelled first.</summary>
    ActiveSubscriptions = 1,

    /// <summary>Subscriptions the owning application marked as never deletable.</summary>
    ProtectedSubscriptions = 2
}

/// <summary>
/// What stands between an organization and deletion. <see cref="OtherMemberCount"/> never blocks
/// deletion; it's surfaced so the owner can see who else loses access before confirming.
/// </summary>
public sealed record OrganizationDeletionReport
{
    public required Guid OrganizationId { get; init; }
    public OrganizationDeletionBlockers Blockers { get; init; }
    public int ActiveSubscriptionCount { get; init; }
    public int ProtectedSubscriptionCount { get; init; }
    public int RemovableSubscriptionCount { get; init; }
    public int OtherMemberCount { get; init; }
    public int PaymentMethodCount { get; init; }

    public bool CanDelete => Blockers == OrganizationDeletionBlockers.None;

    /// <summary>Human-readable blockers, ready to render in a confirmation dialog.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

/// <summary>Thrown when an organization still holds subscriptions that prevent deletion.</summary>
public sealed class OrganizationDeletionBlockedException(OrganizationDeletionReport report)
    : InvalidOperationException(report.Reasons.Count > 0
        ? string.Join(" ", report.Reasons)
        : "This organization can't be deleted yet.")
{
    public OrganizationDeletionReport Report { get; } = report;
}

/// <summary>
/// A member of an organization as the members list renders them: the membership record plus
/// just enough of the user's profile to name and picture the row. Nothing else about the user
/// crosses the organization boundary.
/// </summary>
public sealed record OrganizationMemberProfile
{
    public required Guid UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? EmailAddress { get; init; }
    public string? ProfilePicture { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public bool IsOwner { get; init; }
    public OrganizationMembershipStates State { get; init; } = OrganizationMembershipStates.Active;
    public DateTimeOffset JoinedOn { get; init; }

    /// <summary>The best available label for this member, falling back to a shortened id.</summary>
    public string Label => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName!
        : !string.IsNullOrWhiteSpace(EmailAddress)
            ? EmailAddress!
            : $"Member {UserId.ToString()[..8]}";
}

/// <summary>
/// Everything the account UI shows for one organization in a single round trip: its profile,
/// the caller's standing in it, its members, its subscriptions, and its billing profile.
/// </summary>
public sealed record OrganizationWorkspace
{
    public required CloudLoginOrganization Organization { get; init; }

    /// <summary>The caller owns this organization.</summary>
    public bool IsOwner { get; init; }

    /// <summary>The caller may edit the organization's profile and billing details.</summary>
    public bool CanManage { get; init; }

    /// <summary>The caller's roles within this organization.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<OrganizationMemberProfile> Members { get; init; } = [];
    public IReadOnlyList<AccountSubscription> Subscriptions { get; init; } = [];
    public AccountBillingProfile? BillingProfile { get; init; }

    /// <summary>Deletion readiness. Populated only for callers who may delete the organization.</summary>
    public OrganizationDeletionReport? Deletion { get; init; }
}
