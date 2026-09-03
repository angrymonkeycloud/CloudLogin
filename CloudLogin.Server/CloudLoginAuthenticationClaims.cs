using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Server;

public static class CloudLoginAuthenticationClaims
{
    public const string SecurityStamp = "cloudlogin:security_stamp";
    public const string AuthenticationMethod = "cloudlogin:authentication_method";

    /// <summary>
    /// The session family that represents this browser's sign-in in the <c>Sessions</c> container
    /// - the row the account page shows as "this device". Its session id travels as
    /// <see cref="CloudLoginClaims.SessionId"/> alongside, so every application token minted from
    /// this sign-in can be grouped under the same device.
    /// </summary>
    public const string SessionFamily = "cloudlogin:session_family";

    /// <summary>The browser session a ticket belongs to, or nulls when it was issued without one.</summary>
    public static (string? SessionId, string? FamilyId) SessionOf(ClaimsPrincipal? principal) =>
        (principal?.FindFirst(CloudLoginClaims.SessionId)?.Value,
         principal?.FindFirst(SessionFamily)?.Value);

    /// <summary>Stamps a ticket with the browser session it represents.</summary>
    public static ClaimsPrincipal WithSession(ClaimsPrincipal principal, string sessionId, string familyId)
    {
        ClaimsIdentity identity = principal.Identities.First();

        Replace(identity, CloudLoginClaims.SessionId, sessionId);
        Replace(identity, SessionFamily, familyId);

        return principal;
    }

    /// <summary>
    /// Carries the browser session from one ticket to its replacement. A ticket re-issued for the
    /// same browser - after a security change, or the second sign-in a provider callback performs
    /// - stays the same device rather than becoming a new one.
    /// </summary>
    public static void CarrySession(ClaimsPrincipal? from, ClaimsPrincipal to)
    {
        (string? sessionId, string? familyId) = SessionOf(from);

        if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(familyId))
            WithSession(to, sessionId, familyId);
    }

    private static void Replace(ClaimsIdentity identity, string type, string value)
    {
        foreach (Claim existing in identity.FindAll(type).ToList())
            identity.RemoveClaim(existing);

        identity.AddClaim(new Claim(type, value));
    }

    internal static async Task<ClaimsPrincipal> CreateAsync(
        CloudUser user,
        string authenticationType,
        ICloudLoginStore? store,
        bool markAsLocal = true)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim()),
            new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
            new(ClaimTypes.Surname, user.LastName ?? string.Empty),
            new(AuthenticationMethod, authenticationType)
        ];

        if (markAsLocal)
            claims.Add(new Claim(ClaimTypes.Hash, "CloudLogin"));

        string? email = user.PrimaryEmailAddress?.Input;
        if (!string.IsNullOrWhiteSpace(email))
            claims.Add(new Claim(ClaimTypes.Email, email));

        string? stamp = store is null ? null : await store.GetSecurityStamp(user.Id);
        if (!string.IsNullOrWhiteSpace(stamp))
            claims.Add(new Claim(SecurityStamp, stamp));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

}
