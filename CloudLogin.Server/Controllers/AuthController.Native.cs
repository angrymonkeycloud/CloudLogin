using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using AngryMonkey.CloudLogin.Server.Core.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Server.Controllers;

public sealed record NativeExchangeRequest(string? Handoff, string? Verifier);

/// <summary>
/// A native application signs in through its own backend. The backend is the CloudLogin client, so the native application needs no
/// credential and no registration at the authority: the system browser runs the same Authorization Code + PKCE sign-in as a website, and
/// the session the backend builds is handed to the application over its custom URL scheme as a one-time handoff that only the application
/// that started the sign-in can redeem, because only it holds the verifier for the challenge it began with.
/// </summary>
public partial class AuthController
{
    private const int MinNativeStateLength = 16;
    private const int MaxNativeStateLength = 256;

    private static readonly HashSet<string> NeverNativeSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "javascript", "data", "file", "blob", "vbscript", "ftp", "ws", "wss"
    };

    /// <summary>
    /// Starts a native sign-in in the system browser. <paramref name="challenge"/> is the S256 challenge of a verifier the application keeps;
    /// <paramref name="redirectUri"/> is the application's own URL on a scheme the host lists in <c>NativeCallbackSchemes</c>.
    /// </summary>
    [HttpGet("native/login")]
    public async Task<IActionResult> StartNativeSignIn(
        [FromQuery] string? challenge,
        [FromQuery] string? state,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        CancellationToken cancellationToken = default)
    {
        if (!TryNativeRedirect(redirectUri) || !Pkce.IsValidChallenge(challenge) || !IsNativeState(state))
            return BadRequest(new { error = "invalid_request" });

        NativeLogin native = new(challenge!, redirectUri!, state!);
        IActionResult started = await StartSignInAsync("/", native, cancellationToken);

        // The application is waiting on its browser session; a problem page would leave it waiting, so it is told instead.
        return started is ObjectResult { StatusCode: >= 400 } ? NativeFailure(native, "unavailable") : started;
    }

    /// <summary>Redeems a handoff for the application's session. Sets the session cookie in the response and returns the signed-in person.</summary>
    [HttpPost("native/exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> NativeExchange([FromBody] NativeExchangeRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        if (distributedCache is null || cookieOptions is null || string.IsNullOrWhiteSpace(request.Handoff) || string.IsNullOrWhiteSpace(request.Verifier))
            return BadRequest(new { error = "invalid_request" });

        NativeHandoffs.Handoff? handoff = await new NativeHandoffs(distributedCache).RedeemAsync(request.Handoff, request.Verifier, cancellationToken);

        if (handoff is null)
        {
            _logger.LogWarning("A native handoff was refused: unknown, expired, already used, or the verifier did not match.");
            return BadRequest(new { error = "invalid_grant" });
        }

        AuthenticationTicket? ticket = cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat.Unprotect(handoff.Ticket);
        CloudUser? user = JsonSerializer.Deserialize<CloudUser>(handoff.User, CloudLoginSerialization.Options);

        if (ticket is null || user is null)
            return BadRequest(new { error = "invalid_grant" });

        // Issued afresh now: the cookie's clock starts when the application receives it, not when the browser finished.
        AuthenticationProperties properties = new() { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.Add(_sessionDuration) };

        foreach (KeyValuePair<string, string?> item in ticket.Properties.Items.Where(item => item.Key.StartsWith(".Token.", StringComparison.Ordinal) || item.Key == ".TokenNames"))
            properties.Items[item.Key] = item.Value;

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ticket.Principal, properties);

        // The contract is the CloudLogin serialization, not whatever this host formats JSON with, so a native client reads it the same everywhere.
        return Content(handoff.User, "application/json");
    }

    /// <summary>
    /// Ends the person's session at the authority from the system browser. Like any logout it needs a transaction the application's own
    /// credential opened; a request that comes from another site is refused so a web page cannot start one.
    /// </summary>
    [HttpGet("native/logout")]
    public async Task<IActionResult> StartNativeLogout(
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery] string? state,
        CancellationToken cancellationToken = default)
    {
        if (!TryNativeRedirect(redirectUri) || !IsNativeState(state))
            return BadRequest(new { error = "invalid_request" });

        if (!IsFirstPartyRequest())
        {
            _logger.LogWarning("Rejected a native logout started from another site.");
            return Problem("Sign-out is only accepted from the application's own browser session.", statusCode: StatusCodes.Status403Forbidden);
        }

        NativeLogin native = new(string.Empty, redirectUri!, state!);
        string? publicUrl = PublicBaseUrl();

        if (publicUrl is null || credentials is not { IsConfigured: true })
            return NativeLoggedOut(native, "logout_failed");

        string carried = _transactionProtector.Protect(JsonSerializer.Serialize(native), TransactionLifetime);
        string? logoutUrl = await BeginLogoutAsync($"{publicUrl}/auth/native/logged-out", carried, cancellationToken);

        return logoutUrl is null ? NativeLoggedOut(native, "logout_failed") : Redirect(logoutUrl);
    }

    /// <summary>Where the authority sends the browser after a native logout; hands control back to the application.</summary>
    [HttpGet("native/logged-out")]
    public IActionResult NativeLoggedOutCallback([FromQuery] string? state)
    {
        try
        {
            NativeLogin? native = string.IsNullOrEmpty(state) ? null : JsonSerializer.Deserialize<NativeLogin>(_transactionProtector.Unprotect(state));

            if (native is not null && TryNativeRedirect(native.RedirectUri))
                return NativeLoggedOut(native, null);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            _logger.LogWarning(exception, "A native logout came back with state this application did not issue.");
        }

        return Redirect("/");
    }

    private async Task<IActionResult> CompleteNativeAsync(NativeLogin native, ClaimsPrincipal principal, AuthenticationProperties properties, CloudUser user)
    {
        if (distributedCache is null || cookieOptions is null)
        {
            _logger.LogError("A native sign-in cannot be completed: no distributed cache or cookie options are registered.");
            return NativeFailure(native, "unavailable");
        }

        string ticket = cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat.Protect(
            new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme));

        string handoff = await new NativeHandoffs(distributedCache).CreateAsync(ticket, JsonSerializer.Serialize(user, CloudLoginSerialization.Options), native.Challenge);

        return Redirect(CloudLoginShared.AppendQueryParameter(CloudLoginShared.AppendQueryParameter(native.RedirectUri, "handoff", handoff), "state", native.State));
    }

    private IActionResult NativeFailure(NativeLogin native, string error) =>
        Redirect(CloudLoginShared.AppendQueryParameter(CloudLoginShared.AppendQueryParameter(native.RedirectUri, "error", error), "state", native.State));

    private IActionResult NativeLoggedOut(NativeLogin native, string? error)
    {
        string url = CloudLoginShared.AppendQueryParameter(CloudLoginShared.AppendQueryParameter(native.RedirectUri, "operation", "logout"), "state", native.State);
        return Redirect(error is null ? url : CloudLoginShared.AppendQueryParameter(url, "error", error));
    }

    private bool TryNativeRedirect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) || value.Contains('\\'))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || NeverNativeSchemes.Contains(uri.Scheme))
            return false;

        return serverConfiguration.NativeCallbackSchemes.Any(scheme => scheme.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.Query);
    }

    private static bool IsNativeState(string? state) =>
        state is { Length: >= MinNativeStateLength and <= MaxNativeStateLength } && state.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~');
}

/// <summary>
/// One-time handoffs between the system browser and a native application. Each holds the session the backend built and the challenge of the
/// verifier the application started with; it is spent when presented, and presenting it with the wrong verifier spends it too.
/// </summary>
internal sealed class NativeHandoffs(IDistributedCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public sealed record Handoff(string Ticket, string User);

    private sealed record Entry(string Ticket, string User, string Challenge);

    public async Task<string> CreateAsync(string ticket, string user, string challenge)
    {
        string id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await cache.SetStringAsync(Key(id), JsonSerializer.Serialize(new Entry(ticket, user, challenge)), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
        return id;
    }

    public async Task<Handoff?> RedeemAsync(string id, string verifier, CancellationToken cancellationToken)
    {
        string key = Key(id);
        string? stored = await cache.GetStringAsync(key, cancellationToken);

        if (stored is null)
            return null;

        await cache.RemoveAsync(key, cancellationToken);

        try
        {
            Entry? entry = JsonSerializer.Deserialize<Entry>(stored);

            return entry is not null && Pkce.Verify(entry.Challenge, verifier) ? new Handoff(entry.Ticket, entry.User) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Key(string id) => $"cloudlogin:native:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32]}";
}
