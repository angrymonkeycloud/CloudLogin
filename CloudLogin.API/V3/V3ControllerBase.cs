using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Versioning;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.API.V3;

/// <summary>
/// Base for the V3 façade. V3 speaks only explicit DTOs from <c>AngryMonkey.CloudLogin.V3</c>
/// and runs on the modern storage core; on a deployment that has not configured the core, V3
/// endpoints answer 501 with a clear explanation instead of half-working.
/// </summary>
[ApiController]
[ApiVersionGate(CloudLoginApiVersion.V3)]
public abstract class V3ControllerBase(CloudLoginWebConfiguration configuration, ICloudLogin server) : ControllerBase
{
    protected readonly CloudLoginWebConfiguration Configuration = configuration;
    protected readonly CloudLoginServer Server = (server as CloudLoginServer)!;

    protected bool CoreConfigured => Configuration.Core is not null;

    protected T? CoreService<T>() where T : class =>
        CoreConfigured ? HttpContext.RequestServices.GetService<T>() : null;

    protected ObjectResult CoreUnavailable() => Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "V3 requires the CloudLogin core storage model",
        detail: "This deployment has not configured CloudLoginWebConfiguration.Core. " +
                "Run the storage migration and enable the core to serve API version 3.");

    protected void SetNoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }

    protected async Task<CloudUser?> CurrentUserAsync() => await Server.CurrentUser();

    // ── DTO mapping (the only place a CloudUser becomes a V3 shape) ──────────

    protected static V3SelfProfileResponse ToSelfProfile(CloudUser user) => new()
    {
        UserId = user.ID,
        FirstName = user.FirstName,
        LastName = user.LastName,
        DisplayName = user.DisplayName,
        Username = user.Username,
        DateOfBirth = user.DateOfBirth,
        Country = user.Country,
        Locale = user.Locale,
        ProfilePictureUrl = user.ProfilePicture,
        CreatedOn = user.CreatedOn,
        LastSignedIn = user.LastSignedIn,
        Contacts = ToContacts(user)
    };

    protected static V3PublicUserSummaryResponse ToPublicSummary(CloudUser user) => new()
    {
        UserId = user.ID,
        DisplayName = user.DisplayName,
        ProfilePictureUrl = user.ProfilePicture
    };

    protected static V3AdminUserResponse ToAdminView(CloudUser user) => new()
    {
        UserId = user.ID,
        FirstName = user.FirstName,
        LastName = user.LastName,
        DisplayName = user.DisplayName,
        Username = user.Username,
        Country = user.Country,
        Locale = user.Locale,
        IsLocked = user.IsLocked,
        IsTest = user.IsTest,
        IsGlobalAdmin = user.IsGlobalAdmin,
        CreatedOn = user.CreatedOn,
        LastSignedIn = user.LastSignedIn,
        Contacts = ToContacts(user)
    };

    protected static V3ServiceUserResponse ToServiceView(CloudUser user) => new()
    {
        UserId = user.ID,
        DisplayName = user.DisplayName,
        PrimaryEmail = user.PrimaryEmailAddress?.Input ?? user.EmailAddresses.FirstOrDefault()?.Input,
        Country = user.Country,
        Locale = user.Locale
    };

    private static List<V3ContactModel> ToContacts(CloudUser user) =>
        [.. user.Inputs.Select(input => new V3ContactModel
        {
            Format = input.Format.ToString(),
            Value = input.Input,
            IsPrimary = input.IsPrimary,
            IsVerified = true,
            Providers = [.. input.Providers.Select(provider => provider.Code)]
        })];
}
