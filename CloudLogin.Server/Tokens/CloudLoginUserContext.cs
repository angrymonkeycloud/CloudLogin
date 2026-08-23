using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AngryMonkey.CloudLogin.Server.Tokens;

/// <summary>
/// The one supported way for application code to learn who is making the current
/// request.
/// <para>
/// It reads only from the authenticated principal, which means the answer always
/// comes from a verified credential &mdash; a cookie this server issued, or a token
/// this server verified against the authority's public key. There is no overload
/// that accepts a user id, because accepting one would let a caller choose their
/// own identity.
/// </para>
/// </summary>
public interface ICloudLoginUserContext
{
    /// <summary>The authenticated user's id, or <see langword="null"/> when anonymous.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>Display name from the credential, when present.</summary>
    string? DisplayName { get; }

    string? Email { get; }

    /// <summary>Whether the authority marked this user as a Global Admin.</summary>
    bool IsGlobalAdmin { get; }

    /// <summary>
    /// Sign-in session id. Shared across every token minted for one sign-in, so it
    /// identifies the session to revoke without touching the user's other devices.
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    /// When a backend service is calling on the user's behalf, the calling service's
    /// client id; <see langword="null"/> for a direct call. The subject is still the
    /// user either way, so this is for audit and policy, not for identity.
    /// </summary>
    string? ActingService { get; }

    /// <summary>
    /// The user's id, or a thrown <see cref="UnauthorizedAccessException"/> when the
    /// request is anonymous. Use this at the top of any operation that writes data,
    /// so an unauthenticated call fails loudly instead of recording a null author.
    /// </summary>
    Guid RequireUserId();
}

/// <inheritdoc />
public sealed class CloudLoginUserContext(IHttpContextAccessor accessor) : ICloudLoginUserContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            if (!IsAuthenticated)
                return null;

            // "sub" is what CloudLogin-issued tokens carry; NameIdentifier is what the
            // cookie handler maps it to. Accepting both means one accessor works whether
            // the request arrived with a browser cookie or a bearer token.
            string? value = Principal!.FindFirst(CloudLoginClaims.Subject)?.Value
                            ?? Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(value, out Guid id) && id != Guid.Empty ? id : null;
        }
    }

    public string? DisplayName =>
        Principal?.FindFirst(CloudLoginClaims.Name)?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value;

    public string? Email =>
        Principal?.FindFirst(CloudLoginClaims.Email)?.Value
        ?? Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public bool IsGlobalAdmin =>
        bool.TryParse(Principal?.FindFirst(CloudLoginClaims.IsGlobalAdmin)?.Value, out bool isAdmin) && isAdmin;

    public string? SessionId => Principal?.FindFirst(CloudLoginClaims.SessionId)?.Value;

    public string? ActingService => Principal?.FindFirst(CloudLoginClaims.Actor)?.Value;

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedAccessException(
            "This operation requires an authenticated user, but the request carried no verified identity.");
}
