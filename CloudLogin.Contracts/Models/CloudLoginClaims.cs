namespace AngryMonkey.CloudLogin;

/// <summary>
/// Claim type names used by CloudLogin-issued access tokens.
/// These are the only names a resource server should read identity from;
/// a user identifier that arrives in a request body or query string is data,
/// not identity, and must never be used for authorization.
/// </summary>
public static class CloudLoginClaims
{
    /// <summary>Subject &mdash; the authenticated user's <see cref="CloudUser.Id"/>.</summary>
    public const string Subject = "sub";

    /// <summary>Session identifier, shared by every token minted for one sign-in.</summary>
    public const string SessionId = "sid";

    /// <summary>Unique token identifier, used for replay detection and revocation.</summary>
    public const string TokenId = "jti";

    /// <summary>Display name of the subject.</summary>
    public const string Name = "name";

    /// <summary>Primary email address of the subject.</summary>
    public const string Email = "email";

    /// <summary>Whether the subject holds Global Admin rights in the authority.</summary>
    public const string IsGlobalAdmin = "cl_admin";

    /// <summary>
    /// Actor claim (RFC 8693). Present when a service calls a downstream API
    /// on behalf of the subject; its value is the calling service's client id.
    /// The subject remains the end user, so audit trails stay accurate while
    /// the delegation is still visible.
    /// </summary>
    public const string Actor = "act";

    /// <summary>Space-delimited scopes granted to the token.</summary>
    public const string Scope = "scope";

    /// <summary>Authentication method reference (how the user proved identity).</summary>
    public const string AuthenticationMethod = "amr";
}
