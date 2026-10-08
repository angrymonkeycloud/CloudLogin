using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public sealed record ApplicationView(ApplicationDocument Document, int ActiveSessions);

/// <summary>
/// The applications that use the authority, as seen: nothing is created or configured here. An administrator can block one, which refuses
/// it from then on and ends its sessions, unblock it, end its sessions without blocking it, or forget a record that is no longer relevant.
/// </summary>
public sealed class ApplicationService(
    IApplicationRepository applications,
    ISessionRepository sessions,
    SessionService sessionService,
    IClientDirectory directory,
    IAuditLogger audit,
    TimeProvider? clock = null)
{
    public const int DormantAfterDays = 90;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<ApplicationView>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<ApplicationView> views = [];

        foreach (ApplicationDocument application in (await applications.GetAllAsync(cancellationToken)).OrderByDescending(item => item.LastSeenOn))
            views.Add(new ApplicationView(application, (await sessions.GetActiveFamiliesAsync(application.ClientId, null, 500, cancellationToken)).Count));

        return views;
    }

    public async Task<ApplicationView> GetAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ApplicationDocument application = await applications.GetAsync(clientId, cancellationToken)
            ?? throw AdminException.NotFound("No application with that id has used CloudLogin.");

        return new ApplicationView(application, (await sessions.GetActiveFamiliesAsync(application.ClientId, null, 500, cancellationToken)).Count);
    }

    public bool IsDormant(ApplicationDocument application) => _clock.GetUtcNow() - application.LastSeenOn > TimeSpan.FromDays(DormantAfterDays);

    /// <summary>Refuses the application from now on and ends every session it holds. Returns how many sessions ended.</summary>
    public async Task<int> BlockAsync(Guid actorUserId, string clientId, string? reason, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        await MutateAsync(clientId, application =>
        {
            application.IsBlocked = true;
            application.BlockedOn = now;
            application.BlockedByUserId = actorUserId.ToString();
            application.BlockReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 200)];
        }, cancellationToken);

        int ended = await EndSessionsAsync(clientId, cancellationToken);

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.ApplicationBlocked,
            ActorUserId = actorUserId,
            ClientId = clientId,
            Data = new Dictionary<string, string> { ["SessionsEnded"] = ended.ToString() }
        }, cancellationToken);

        return ended;
    }

    public async Task UnblockAsync(Guid actorUserId, string clientId, CancellationToken cancellationToken = default)
    {
        await MutateAsync(clientId, application =>
        {
            application.IsBlocked = false;
            application.BlockedOn = null;
            application.BlockedByUserId = null;
            application.BlockReason = null;
        }, cancellationToken);

        await audit.LogAsync(new AuditEntry { EventType = AuditEventTypes.ApplicationUnblocked, ActorUserId = actorUserId, ClientId = clientId }, cancellationToken);
    }

    /// <summary>Ends every session the application holds without blocking it. Returns how many ended.</summary>
    public async Task<int> RevokeSessionsAsync(Guid actorUserId, string clientId, CancellationToken cancellationToken = default)
    {
        await GetAsync(clientId, cancellationToken);
        int ended = await EndSessionsAsync(clientId, cancellationToken);

        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.SessionsRevokedForApplication,
            ActorUserId = actorUserId,
            ClientId = clientId,
            Data = new Dictionary<string, string> { ["Revoked"] = ended.ToString() }
        }, cancellationToken);

        return ended;
    }

    /// <summary>Removes the record. A blocked application must be unblocked first, so forgetting it never lifts the block by accident.</summary>
    public async Task ForgetAsync(Guid actorUserId, string clientId, CancellationToken cancellationToken = default)
    {
        ApplicationDocument application = (await GetAsync(clientId, cancellationToken)).Document;

        if (application.IsBlocked)
            throw AdminException.Invalid("Unblock the application before forgetting it; forgetting a blocked application would let it back in.");

        await applications.DeleteAsync(clientId, cancellationToken);
        directory.Invalidate();

        await audit.LogAsync(new AuditEntry { EventType = AuditEventTypes.ApplicationForgotten, ActorUserId = actorUserId, ClientId = clientId }, cancellationToken);
    }

    private async Task<int> EndSessionsAsync(string clientId, CancellationToken cancellationToken)
    {
        int ended = 0;

        // Bounded passes, so an application that keeps signing people in cannot hold this open forever.
        for (int pass = 0; pass < 20; pass++)
        {
            List<SessionFamilyDocument> families = await sessions.GetActiveFamiliesAsync(clientId, null, 500, cancellationToken);

            if (families.Count == 0)
                break;

            foreach (SessionFamilyDocument family in families)
            {
                await sessionService.RevokeFamilyAsync(family.FamilyId, SessionRevocationReasons.AdminRevoked, cancellationToken);
                ended++;
            }
        }

        return ended;
    }

    private async Task MutateAsync(string clientId, Action<ApplicationDocument> change, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ApplicationDocument application = await applications.GetAsync(clientId, cancellationToken)
                ?? throw AdminException.NotFound("No application with that id has used CloudLogin.");

            change(application);

            if (await applications.TryReplaceAsync(application, cancellationToken))
            {
                directory.Invalidate();
                return;
            }
        }

        throw AdminException.Conflict("The application changed while it was being updated. Try again.");
    }
}
