using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Server;

public static class CloudLoginAuthenticationClaims
{
    public const string SecurityStamp = "cloudlogin:security_stamp";
    public const string AuthenticationMethod = "cloudlogin:authentication_method";

    internal static async Task<ClaimsPrincipal> CreateAsync(
        CloudUser user,
        string authenticationType,
        ICloudLoginStore? store,
        bool markAsLocal = true)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.ID.ToString()),
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

        string? stamp = store is null ? null : await store.GetSecurityStamp(user.ID);
        if (!string.IsNullOrWhiteSpace(stamp))
            claims.Add(new Claim(SecurityStamp, stamp));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

}
