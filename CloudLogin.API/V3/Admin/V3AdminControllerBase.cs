using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.API.V3.Admin;

/// <summary>
/// Shared plumbing for the control-plane endpoints. Every action names the permission it needs and
/// runs through <see cref="AdminAsync"/>, which resolves the caller's roles from the stored account
/// (never from a claim), refuses anyone without that permission, and turns the application layer's
/// errors into HTTP statuses.
/// </summary>
[Authorize]
public abstract class V3AdminControllerBase(CloudLoginWebConfiguration configuration, ICloudLogin server)
    : V3ControllerBase(configuration, server)
{
    protected CancellationToken Cancellation => HttpContext.RequestAborted;

    protected T Service<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    protected async Task<IActionResult> AdminAsync(string? permission, Func<AdminAccess, Task<IActionResult>> action)
    {
        SetNoStore();

        IAdminAuthorizer? authorizer = CoreService<IAdminAuthorizer>();

        if (authorizer is null)
            return CoreUnavailable();

        CloudUser? user = await CurrentUserAsync();

        if (user is null || user.Id == Guid.Empty || user.IsLocked)
            return Unauthorized();

        try
        {
            AdminAccess access = permission is null
                ? await authorizer.GetAccessAsync(user.Id, Cancellation)
                : await authorizer.RequireAsync(user.Id, permission, Cancellation);

            if (!access.IsAdministrator)
                throw AdminException.Forbidden("This account is not a CloudLogin administrator.");

            return await action(access);
        }
        catch (AdminException exception)
        {
            return exception.Kind switch
            {
                AdminErrorKinds.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: exception.Message),
                AdminErrorKinds.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: exception.Message),
                AdminErrorKinds.Forbidden => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not permitted", detail: exception.Message),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: exception.Message)
            };
        }
    }

    protected static T ParseEnum<T>(string? value, string field) where T : struct, Enum =>
        value is not null && Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw AdminException.Invalid($"'{value}' is not a valid {field}.");

    protected static List<T>? ParseEnums<T>(IEnumerable<string>? values, string field) where T : struct, Enum =>
        values?.Select(value => ParseEnum<T>(value, field)).Distinct().ToList();
}

/// <summary>Application-layer views to V3 DTOs. The only place a document becomes a response.</summary>
internal static class V3AdminMapper
{
    public static V3AdminAuditEventModel ToModel(AuditEventDocument item) => new()
    {
        EventId = item.Id,
        EventType = item.EventType,
        OccurredOn = item.OccurredOn,
        Result = item.Result,
        UserId = Guid.TryParse(item.UserId, out Guid userId) ? userId : null,
        ActorUserId = Guid.TryParse(item.ActorUserId, out Guid actorId) ? actorId : null,
        ClientId = item.ClientId,
        IpAddress = item.IpAddress,
        Data = item.Data is null ? [] : new Dictionary<string, string>(item.Data)
    };

    public static V3AdminUserSummaryResponse ToModel(AdminUserSummary user) => new()
    {
        UserId = user.UserId,
        DisplayName = user.DisplayName,
        PrimaryContact = user.PrimaryContact,
        State = user.State.ToString(),
        IsLocked = user.IsLocked,
        IsGlobalAdmin = user.IsGlobalAdmin,
        Roles = [.. user.Roles.Select(role => role.ToString())],
        CreatedOn = user.CreatedOn,
        LastSignedInOn = user.LastSignedInOn
    };

    public static V3AdminUserDetailResponse ToModel(AdminUserDetail detail) => new()
    {
        Summary = ToModel(detail.Summary),
        Username = detail.Username,
        Country = detail.Country,
        Locale = detail.Locale,
        Contacts =
        [
            .. detail.Contacts.Select(contact => new V3ContactModel
            {
                Format = contact.Format,
                Value = contact.Value,
                IsPrimary = contact.IsPrimary,
                IsVerified = contact.IsVerified,
                Providers = [.. contact.ProviderCodes]
            })
        ],
        HasLocalCredential = detail.HasPassword,
        HasAuthenticatorApp = detail.HasAuthenticatorApp,
        Passkeys =
        [
            .. detail.Passkeys.Select(passkey => new V3AdminPasskeyModel
            {
                CredentialId = passkey.CredentialId,
                Name = passkey.Name,
                IsBackedUp = passkey.IsBackedUp,
                RegisteredOn = passkey.RegisteredOn,
                LastUsedOn = passkey.LastUsedOn
            })
        ],
        LinkedIdentities =
        [
            .. detail.LinkedIdentities.Select(identity => new V3AdminLinkedIdentityModel
            {
                CredentialId = identity.CredentialId,
                ProviderCode = identity.ProviderCode,
                ProviderEmail = identity.ProviderEmail,
                EmailIsVerified = identity.EmailIsVerified,
                LinkedOn = identity.LinkedOn
            })
        ]
    };

    public static V3AdminApplicationResponse ToModel(ApplicationView view, bool isDormant) => new()
    {
        ClientId = view.Document.ClientId,
        Kind = view.Document.Kind.ToString(),
        Origins = [.. view.Document.Origins],
        BackChannelLogoutUri = view.Document.BackChannelLogoutUri,
        FirstSeenOn = view.Document.FirstSeenOn,
        LastSeenOn = view.Document.LastSeenOn,
        LastKeyId = view.Document.LastSecretKeyId,
        ActiveSessions = view.ActiveSessions,
        IsDormant = isDormant,
        IsBlocked = view.Document.IsBlocked,
        BlockedOn = view.Document.BlockedOn,
        BlockReason = view.Document.BlockReason
    };

    public static V3AdminSecretKeyModel ToModel(SecretKeyView view) => new()
    {
        Id = view.Document.Id,
        Label = view.Document.Label,
        Prefix = view.Document.Prefix,
        IsDeployment = view.Document.IsDeployment,
        Status = view.Status.ToString(),
        CreatedOn = view.Document.CreatedOn == default ? null : view.Document.CreatedOn,
        ExpiresOn = view.Document.ExpiresOn,
        LastUsedOn = view.Document.LastUsedOn,
        LastUsedBy = view.Document.LastUsedBy,
        RevokedOn = view.Document.RevokedOn,
        RevocationReason = view.Document.RevocationReason
    };

    public static V3AdminSessionResponse ToModel(AdminSessionView session) => new()
    {
        FamilyId = session.FamilyId,
        SessionId = session.SessionId,
        UserId = session.UserId,
        UserName = session.UserName,
        Audience = session.Audience,
        ApplicationClientId = session.ApplicationClientId,
        ApplicationName = session.ApplicationName,
        Scope = session.Scope,
        CreatedOn = session.CreatedOn,
        LastSeenOn = session.LastSeenOn,
        ExpiresOn = session.ExpiresOn,
        DeviceName = session.DeviceName,
        Browser = session.Browser,
        OperatingSystem = session.OperatingSystem,
        CreatedByIp = session.CreatedByIp,
        LastSeenIp = session.LastSeenIp,
        IsAuthoritySession = session.IsAuthoritySession,
        IsRevoked = session.IsRevoked,
        RevocationReason = session.RevocationReason
    };

    public static V3AdminKeyModel ToModel(SigningKeyView key) => new()
    {
        KeyId = key.KeyId,
        Status = key.Status.ToString(),
        CreatedOn = key.CreatedOn,
        SigningExpiresOn = key.SigningExpiresOn,
        PublishExpiresOn = key.PublishExpiresOn,
        ExpiresSoon = key.ExpiresSoon
    };

    public static V3AdministratorResponse ToModel(AdministratorView administrator) => new()
    {
        UserId = administrator.UserId,
        DisplayName = administrator.DisplayName,
        PrimaryContact = administrator.PrimaryContact,
        IsGlobalAdmin = administrator.IsGlobalAdmin,
        IsLocked = administrator.IsLocked,
        Roles = [.. administrator.Roles.Select(role => role.ToString())]
    };
}
