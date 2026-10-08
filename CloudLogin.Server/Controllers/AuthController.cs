using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using AngryMonkey.CloudLogin.Server.Tokens;

namespace AngryMonkey.CloudLogin.Server.Controllers;

[ApiController]
[Route("auth")]
public partial class AuthController(
    IConfiguration configuration,
    CloudLoginServerConfiguration serverConfiguration,
    IHttpClientFactory httpClientFactory,
    ILogger<AuthController> logger,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<CloudLoginTokenClientOptions>? tokenOptions = null,
    ICloudLoginTokenProvider? tokenProvider = null,
    ICloudLoginClientCredentials? credentials = null,
    ICloudLoginSessionRevocations? revocations = null,
    CloudLoginLogoutTokenValidator? logoutTokenValidator = null,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null,
    IOptionsMonitor<CookieAuthenticationOptions>? cookieOptions = null) : ControllerBase
{
    private sealed record NativeLogin(string Challenge, string RedirectUri, string State);

    private sealed record LoginTransaction(string State, string Verifier, string ReturnUrl, NativeLogin? Native = null);

    private TimeSpan TransactionLifetime => _tokenOptions?.LoginTransactionLifetime ?? TimeSpan.FromMinutes(10);

    private readonly CloudLoginTokenClientOptions? _tokenOptions = tokenOptions?.Value;
    private readonly string _loginBaseUrl = (serverConfiguration.LoginUrl ?? configuration["LoginUrl"] ?? throw new InvalidOperationException("LoginUrl configuration is missing.")).TrimEnd('/');
    private readonly TimeSpan _sessionDuration = serverConfiguration.SessionDuration;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<AuthController> _logger = logger;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("AngryMonkey.CloudLogin.Consumer.ReturnState.v1");
    private readonly ITimeLimitedDataProtector _transactionProtector = dataProtectionProvider.CreateProtector("AngryMonkey.CloudLogin.Consumer.LoginTransaction.v1").ToTimeLimitedDataProtector();

    [HttpGet("login")]
    public Task<IActionResult> Login([FromQuery] string? returnUrl = null, CancellationToken cancellationToken = default) =>
        StartSignInAsync(NormalizeLocalReturnUrl(returnUrl), null, cancellationToken);

    private async Task<IActionResult> StartSignInAsync(string returnUrl, NativeLogin? native, CancellationToken cancellationToken)
    {
        if (_tokenOptions is null || credentials is not { IsConfigured: true })
            return Problem("This application has no CloudLogin client credential configured, so it cannot start a sign-in.", statusCode: StatusCodes.Status501NotImplemented);

        string? publicUrl = PublicBaseUrl();

        if (publicUrl is null)
            return Problem("This application's public URL is not configured (CloudLogin:PublicUrl), so it cannot name a trusted callback.", statusCode: StatusCodes.Status500InternalServerError);

        string state = CloudLoginPkce.CreateState();
        string verifier = CloudLoginPkce.CreateVerifier();

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            HttpClient client = _httpClientFactory.CreateClient(CloudLoginTokenClientOptions.HttpClientName);

            using HttpRequestMessage request = new(HttpMethod.Post, $"{_loginBaseUrl}/CloudLogin/Authorize/Begin")
            {
                Content = JsonContent.Create(new
                {
                    returnUrl = CallbackUrl(publicUrl),
                    state,
                    codeChallenge = CloudLoginPkce.CreateChallenge(verifier),
                    codeChallengeMethod = CloudLoginPkce.Method,
                    // Where this backend is told a session ended. On its own origin, so nothing needs registering anywhere.
                    backChannelLogoutUri = $"{publicUrl}/auth/backchannel-logout"
                })
            };

            await credentials.ApplyAsync(request, timeout.Token);
            using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("CloudLogin refused to start a sign-in transaction (status {Status}). Sign-in is not attempted without one.", response.StatusCode);
                return Problem("The sign-in could not be started.", statusCode: StatusCodes.Status502BadGateway);
            }

            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);

            if (!body.RootElement.TryGetProperty("transaction", out JsonElement reference) ||
                reference.GetString() is not { } transaction ||
                !Core.Application.AuthorizationTransactionService.TryParseReference(transaction, out _))
                return Problem("The sign-in could not be started.", statusCode: StatusCodes.Status502BadGateway);

            AppendTransactionCookie(state, new LoginTransaction(state, verifier, returnUrl, native));

            return Redirect($"{_loginBaseUrl}/?referer={Uri.EscapeDataString(transaction)}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(exception, "The authority could not be reached to start a sign-in.");
            return Problem("The sign-in could not be started.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken = default)
    {
        LoginTransaction? transaction = TakeTransaction(state);

        if (transaction is null)
        {
            _logger.LogWarning("A sign-in callback arrived without a matching, unexpired transaction from this browser and was rejected.");
            return Redirect("/?error=invalid_callback");
        }

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Login callback received error: {Error}", error);
            return Failure(transaction, "login_failed");
        }

        string? publicUrl = PublicBaseUrl();

        if (string.IsNullOrWhiteSpace(code) || publicUrl is null || credentials is not { IsConfigured: true })
            return Failure(transaction, "invalid_callback");

        CloudLoginTokenResponse? tokens = await RedeemAsync(code, transaction.Verifier, CallbackUrl(publicUrl), cancellationToken);
        CloudUser? user = tokens?.User;

        if (tokens is null || user is null)
        {
            _logger.LogWarning("The authorization code could not be redeemed.");
            return Failure(transaction, "user_not_found");
        }

        (ClaimsPrincipal principal, AuthenticationProperties properties) = BuildSession(tokens, user, persistent: transaction.Native is not null);

        // A sign-in a native application started ends at the application, not in this browser: its session is handed over, never set here.
        if (transaction.Native is not null)
            return await CompleteNativeAsync(transaction.Native, principal, properties, user);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);

        return Redirect(transaction.ReturnUrl);
    }

    private IActionResult Failure(LoginTransaction transaction, string error) =>
        transaction.Native is not null ? NativeFailure(transaction.Native, error) : Redirect($"/?error={error}");

    /// <summary>The application's session for a signed-in person: the claims its cookie carries, and the tokens kept inside that cookie.</summary>
    private (ClaimsPrincipal Principal, AuthenticationProperties Properties) BuildSession(CloudLoginTokenResponse tokens, CloudUser user, bool persistent)
    {
        string? sessionId = ReadSessionId(tokens.AccessToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName ?? user.FirstName ?? "User"),
            new(ClaimTypes.Email, user.PrimaryEmailAddress?.Input ?? ""),
            new(CloudLoginSessionClaims.IssuedAt, (tokens.IssuedAtUnixMs ?? now.ToUnixTimeMilliseconds()).ToString())
        ];

        if (!string.IsNullOrEmpty(sessionId))
            claims.Add(new Claim(CloudLoginSessionClaims.SessionId, sessionId));

        if (!string.IsNullOrEmpty(user.FirstName))
            claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));
        if (!string.IsNullOrEmpty(user.LastName))
            claims.Add(new Claim(ClaimTypes.Surname, user.LastName));
        if (!string.IsNullOrWhiteSpace(user.ProfilePicture))
            claims.Add(new Claim("picture", user.ProfilePicture));
        if (!string.IsNullOrWhiteSpace(user.Country))
            claims.Add(new Claim("country", user.Country));
        if (!string.IsNullOrWhiteSpace(user.Locale))
            claims.Add(new Claim("locale", user.Locale));

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        // A native application keeps its cookie for as long as the session lasts; a browser's lasts as long as the browser.
        AuthenticationProperties properties = persistent
            ? new() { IsPersistent = true, ExpiresUtc = now.Add(_sessionDuration) }
            : new() { IsPersistent = false, ExpiresUtc = null };

        properties.StoreTokens(
        [
            new AuthenticationToken { Name = CloudLoginTokenProvider.AccessTokenName, Value = tokens.AccessToken },
            new AuthenticationToken { Name = CloudLoginTokenProvider.RefreshTokenName, Value = tokens.RefreshToken ?? string.Empty },
            new AuthenticationToken { Name = CloudLoginTokenProvider.ExpiresOnName, Value = now.AddSeconds(tokens.ExpiresIn).ToString("o") }
        ]);

        return (principal, properties);
    }

    [HttpPost("backchannel-logout")]
    [AllowAnonymous]
    public async Task<IActionResult> BackChannelLogout([FromForm(Name = "logout_token")] string? logoutToken, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        if (revocations is null || logoutTokenValidator is null || string.IsNullOrWhiteSpace(logoutToken))
            return BadRequest(new { error = "invalid_request" });

        LogoutTokenResult result = await logoutTokenValidator.ValidateAsync(logoutToken, cancellationToken);

        if (!result.Valid)
        {
            _logger.LogWarning("A back-channel logout notification was rejected: {Reason}.", result.Failure);
            return BadRequest(new { error = "invalid_request" });
        }

        try
        {
            if (await revocations.IsLogoutTokenProcessedAsync(result.TokenId!, cancellationToken))
                return Ok();

            // Applying a notification is idempotent (a session id stays revoked; a subject is revoked up to the authority's own
            // timestamp in the token), so two instances applying the same token at once, or a repeat, change nothing further. The token
            // is marked processed only after the revocation is stored, so a failed write leaves it retryable instead of acknowledged.
            if (!string.IsNullOrEmpty(result.SessionId))
                await revocations.RevokeSessionAsync(result.SessionId, cancellationToken);
            else if (!string.IsNullOrEmpty(result.Subject))
                await revocations.RevokeSubjectAsync(result.Subject, result.RevokedBefore ?? DateTimeOffset.UtcNow, cancellationToken);

            await revocations.MarkLogoutTokenProcessedAsync(result.TokenId!, result.ExpiresOn, cancellationToken);
            return Ok();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "A valid back-channel logout notification could not be applied; the authority is asked to retry.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "temporarily_unavailable" });
        }
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile([FromQuery] string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return await Login($"/auth/profile?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}", cancellationToken);

        string? publicUrl = PublicBaseUrl();

        if (publicUrl is null)
            return Problem("This application's public URL is not configured (CloudLogin:PublicUrl).", statusCode: StatusCodes.Status500InternalServerError);

        string callbackUrl = $"{publicUrl}/auth/profileCallback?state={Uri.EscapeDataString(_stateProtector.Protect(NormalizeLocalReturnUrl(returnUrl)))}";

        return Redirect($"{_loginBaseUrl}/Account?referer={Uri.EscapeDataString(callbackUrl)}");
    }

    [HttpGet("profileCallback")]
    public IActionResult ProfileCallback([FromQuery] string? state)
    {
        try
        {
            return Redirect(NormalizeLocalReturnUrl(DecodeReturnUrl(state)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during profile callback");
            return Redirect("/");
        }
    }

    [HttpGet("token")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Token([FromQuery] string? audience = null, CancellationToken cancellationToken = default)
    {
        if (tokenProvider is null || _tokenOptions is null)
            return Problem("This application is not configured to issue downstream tokens.", statusCode: StatusCodes.Status501NotImplemented);

        AuthenticateResult session = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        if (!session.Succeeded)
            return Unauthorized();

        if (!IsFirstPartyRequest())
        {
            _logger.LogWarning("Rejected a cross-site downstream token request.");
            return Problem("Token requests are only served to this application's own front end.", statusCode: StatusCodes.Status403Forbidden);
        }

        string requested = string.IsNullOrWhiteSpace(audience) ? _tokenOptions.Audience : audience;
        string? accessToken = await tokenProvider.GetAccessTokenAsync(requested, forceRefresh: false, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            return Problem($"No token could be issued for audience '{requested}'.", statusCode: StatusCodes.Status403Forbidden);

        return Ok(new CloudLoginDownstreamTokenResponse { AccessToken = accessToken, ExpiresIn = RemainingLifetimeSeconds(accessToken), Audience = requested });
    }

    [HttpPost("logout")]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout([FromQuery] string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (!IsFirstPartyRequest())
        {
            _logger.LogWarning("Rejected a cross-site logout request.");
            return Problem("Sign-out is only accepted from this application's own pages.", statusCode: StatusCodes.Status403Forbidden);
        }

        returnUrl = NormalizeLocalReturnUrl(returnUrl);
        string? sessionId = (await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Principal?.FindFirst(CloudLoginSessionClaims.SessionId)?.Value;

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        if (revocations is not null && !string.IsNullOrEmpty(sessionId))
            await revocations.RevokeSessionAsync(sessionId, cancellationToken);

        string? publicUrl = PublicBaseUrl();
        string? logoutUrl = publicUrl is null || credentials is not { IsConfigured: true } ? null : await BeginLogoutAsync($"{publicUrl}{returnUrl}", null, cancellationToken);

        return Redirect(logoutUrl ?? returnUrl);
    }

    private async Task<string?> BeginLogoutAsync(string postLogoutRedirectUri, string? state, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            HttpClient client = _httpClientFactory.CreateClient(CloudLoginTokenClientOptions.HttpClientName);

            using HttpRequestMessage request = new(HttpMethod.Post, $"{_loginBaseUrl}/CloudLogin/Authorize/Logout")
            {
                Content = JsonContent.Create(new { postLogoutRedirectUri, state })
            };

            await credentials!.ApplyAsync(request, timeout.Token);
            using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CloudLogin did not accept a logout transaction (status {Status}); the local session is ended but the authority session is not.", response.StatusCode);
                return null;
            }

            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);

            return body.RootElement.TryGetProperty("transaction", out JsonElement reference) &&
                   reference.GetString() is { } transaction &&
                   Core.Application.AuthorizationTransactionService.TryParseReference(transaction, out _)
                ? $"{_loginBaseUrl}/CloudLogin/Logout?referer={Uri.EscapeDataString(transaction)}"
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "The authority could not be reached to sign out; the local session is ended but the authority session is not.");
            return null;
        }
    }

    private async Task<CloudLoginTokenResponse?> RedeemAsync(string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(CloudLoginTokenClientOptions.HttpClientName);

            using HttpRequestMessage request = new(HttpMethod.Post, $"{_loginBaseUrl}/CloudLogin/Token/Code")
            {
                Content = JsonContent.Create(new CloudLoginCodeRequest
                {
                    Code = code,
                    CodeVerifier = verifier,
                    RedirectUri = redirectUri,
                    Audience = _tokenOptions?.Audience
                })
            };

            await credentials!.ApplyAsync(request, cancellationToken);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Authorization code redemption failed with status {Status}.", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CloudLoginTokenResponse>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(exception, "Error redeeming the authorization code.");
            return null;
        }
    }

    private string? PublicBaseUrl()
    {
        string? configured = _tokenOptions?.PublicUrl;

        if (!string.IsNullOrWhiteSpace(configured))
            return Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback) && string.IsNullOrEmpty(uri.UserInfo)
                ? uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                : null;

        string host = Request.Host.Host;
        bool loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "[::1]" || host == "::1";

        return loopback ? $"{Request.Scheme}://{Request.Host}" : null;
    }

    private static string CallbackUrl(string publicUrl) => $"{publicUrl}/auth/callback";

    private string TransactionCookieName(string state)
    {
        string prefix = Request.IsHttps ? serverConfiguration.CookieName : serverConfiguration.CookieName.Replace("__Host-", string.Empty, StringComparison.Ordinal);
        return $"{prefix}.tx.{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)))[..16]}";
    }

    private void AppendTransactionCookie(string state, LoginTransaction transaction)
    {
        foreach (string stale in Request.Cookies.Keys.Where(name => name.Contains(".tx.", StringComparison.Ordinal)).Skip(4))
            Response.Cookies.Delete(stale);

        Response.Cookies.Append(TransactionCookieName(state), _transactionProtector.Protect(JsonSerializer.Serialize(transaction), TransactionLifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            MaxAge = TransactionLifetime
        });
    }

    private LoginTransaction? TakeTransaction(string? state)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length is < 16 or > 512)
            return null;

        string name = TransactionCookieName(state);

        if (!Request.Cookies.TryGetValue(name, out string? protectedValue) || string.IsNullOrEmpty(protectedValue))
            return null;

        Response.Cookies.Delete(name, new CookieOptions { Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/" });

        try
        {
            LoginTransaction? transaction = JsonSerializer.Deserialize<LoginTransaction>(_transactionProtector.Unprotect(protectedValue));

            return transaction is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(transaction.State), Encoding.UTF8.GetBytes(state)) ? transaction : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static string? ReadSessionId(string accessToken)
    {
        try
        {
            return new JsonWebToken(accessToken).TryGetClaim(CloudLoginClaims.SessionId, out Claim? claim) ? claim.Value : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private bool IsFirstPartyRequest()
    {
        string? fetchSite = Request.Headers["Sec-Fetch-Site"].FirstOrDefault();

        if (!string.IsNullOrEmpty(fetchSite) &&
            !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase))
            return false;

        string? origin = Request.Headers.Origin.FirstOrDefault();

        if (string.IsNullOrEmpty(origin))
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri)
            && string.Equals(originUri.Scheme, Request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(originUri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static int RemainingLifetimeSeconds(string accessToken)
    {
        try
        {
            double seconds = (new JsonWebToken(accessToken).ValidTo - DateTime.UtcNow).TotalSeconds;
            return seconds > 0 ? (int)seconds : 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }

    private string DecodeReturnUrl(string? state)
    {
        if (!string.IsNullOrEmpty(state))
        {
            try
            {
                return _stateProtector.Unprotect(state);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode state parameter");
            }
        }

        return "/";
    }

    private string NormalizeLocalReturnUrl(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
