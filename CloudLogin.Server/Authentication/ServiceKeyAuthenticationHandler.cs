using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server;

public static class ServiceKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "ServiceKey";
    public const string HeaderName = "X-CloudLogin-ServiceKey";
}

/// <summary>
/// Authenticates trusted backend callers (not browsers) via a shared-secret header, for
/// service-to-service lookups such as <c>CloudLogin/Service/Workspaces/{id}</c>. Cookie
/// authentication for interactive users is a completely separate scheme and is unaffected.
/// </summary>
public class ServiceKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CloudLoginWebConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ServiceKeyAuthenticationDefaults.HeaderName, out var headerValues))
            return Task.FromResult(AuthenticateResult.Fail("Missing service key header."));

        string providedKey = headerValues.ToString();

        if (string.IsNullOrWhiteSpace(providedKey) ||
            configuration.ServiceKeys.Count == 0 ||
            !configuration.ServiceKeys.Any(key => TimingSafeEquals(key, providedKey)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid service key."));

        ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, "service")], ServiceKeyAuthenticationDefaults.AuthenticationScheme);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, ServiceKeyAuthenticationDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool TimingSafeEquals(string a, string b)
    {
        byte[] bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        byte[] bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
