using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public sealed record AdminSessionView(
    string FamilyId,
    string SessionId,
    Guid UserId,
    string? UserName,
    string? Audience,
    string? ApplicationClientId,
    string? ApplicationName,
    string? Scope,
    DateTimeOffset CreatedOn,
    DateTimeOffset? LastSeenOn,
    DateTimeOffset ExpiresOn,
    string? DeviceName,
    string? Browser,
    string? OperatingSystem,
    string? CreatedByIp,
    string? LastSeenIp,
    bool IsAuthoritySession,
    bool IsRevoked,
    string? RevocationReason);

/// <summary>Sessions across the realm, and the ways an administrator ends them.</summary>
public sealed class AdminSessionService(
    ISessionRepository sessions,
    SessionService sessionService,
    IUserRepository users,
    ApplicationService applications,
    IAuditLogger audit)
{
    public async Task<IReadOnlyList<AdminSessionView>> ListActiveAsync(
        string? audience, Guid? userId, int take, CancellationToken cancellationToken = default)
    {
        List<SessionFamilyDocument> families = await sessions.GetActiveFamiliesAsync(audience, userId, take, cancellationToken);
        return await ToViewsAsync(families, cancellationToken);
    }

    /// <summary>Every session a user has had, newest first, revoked ones included so the reason is visible.</summary>
    public async Task<IReadOnlyList<AdminSessionView>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<SessionFamilyDocument> families = await sessions.GetFamiliesForUserAsync(userId, cancellationToken);
        return await ToViewsAsync([.. families.OrderByDescending(family => family.CreatedOn)], cancellationToken);
    }

    public async Task RevokeAsync(Guid actorUserId, string familyId, CancellationToken cancellationToken = default)
    {
        SessionFamilyDocument family = await sessions.GetFamilyAsync(familyId, cancellationToken)
            ?? throw AdminException.NotFound("That session does not exist or has already expired.");

        await sessionService.RevokeFamilyAsync(familyId, SessionRevocationReasons.AdminRevoked, cancellationToken);

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.SessionRevoked,
            UserId = Guid.TryParse(family.UserId, out Guid userId) ? userId : null,
            ActorUserId = actorUserId,
            Data = new Dictionary<string, string> { ["Audience"] = family.Audience ?? string.Empty, ["FamilyId"] = familyId }
        }, cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(userId, cancellationToken);
        await sessionService.RevokeAllForUserAsync(userId, SessionRevocationReasons.AdminRevoked, cancellationToken);

        await audit.LogAsync(new AuditEntry { EventType = AuditEventTypes.SessionsRevokedForUser, UserId = userId, ActorUserId = actorUserId }, cancellationToken);
    }

    /// <summary>
    /// Global sign-out: every session ends, and the security stamp rotates so any cookie that
    /// slipped past is refused too. The person signs back in everywhere.
    /// </summary>
    public async Task SignOutEverywhereAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        UserDocument user = await RequireUserAsync(userId, cancellationToken);

        await sessionService.RevokeAllForUserAsync(userId, SessionRevocationReasons.AdminRevoked, cancellationToken);

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedOn = DateTimeOffset.UtcNow;

        try
        {
            await users.ReplaceAsync(user, cancellationToken);
        }
        catch (CoreConcurrencyException)
        {
            throw AdminException.Conflict("The account changed while signing it out. Try again.");
        }

        await audit.LogAsync(new AuditEntry { EventType = AuditEventTypes.UserSignedOutEverywhere, UserId = userId, ActorUserId = actorUserId }, cancellationToken);
    }

    /// <summary>Ends every session issued to one application, for when it is compromised. Returns how many ended.</summary>
    public Task<int> RevokeForApplicationAsync(Guid actorUserId, string clientId, CancellationToken cancellationToken = default) =>
        applications.RevokeSessionsAsync(actorUserId, clientId, cancellationToken);

    private async Task<UserDocument> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.GetAsync(userId, cancellationToken) ?? throw AdminException.NotFound("That account does not exist.");

    private async Task<IReadOnlyList<AdminSessionView>> ToViewsAsync(IReadOnlyList<SessionFamilyDocument> families, CancellationToken cancellationToken)
    {
        Dictionary<string, string?> names = [];
        List<AdminSessionView> views = [];

        foreach (SessionFamilyDocument family in families)
        {
            if (!Guid.TryParse(family.UserId, out Guid userId))
                continue;

            if (!names.ContainsKey(family.UserId))
            {
                UserDocument? user = await users.GetAsync(userId, cancellationToken);
                names[family.UserId] = user is null ? null : CloudLoginDisplayName.Compose(user.FirstName, user.LastName) ?? user.Username;
            }

            string audience = family.Audience ?? string.Empty;
            bool authority = string.Equals(audience, SessionService.BrowserAudience, StringComparison.OrdinalIgnoreCase);

            views.Add(new AdminSessionView(
                family.FamilyId,
                family.SessionId,
                userId,
                names[family.UserId],
                family.Audience,
                authority ? null : family.ClientId ?? family.Audience,
                authority ? "CloudLogin" : family.ClientId ?? family.Audience,
                family.Scope,
                family.CreatedOn,
                family.LastSeenOn,
                family.ExpiresOn ?? family.CreatedOn,
                family.DeviceName,
                family.DeviceBrowser,
                family.DeviceOperatingSystem,
                family.CreatedByIp,
                family.LastSeenIp,
                authority,
                family.IsRevoked,
                family.IsRevoked ? family.RevocationReason.ToString() : null));
        }

        return views;
    }
}
