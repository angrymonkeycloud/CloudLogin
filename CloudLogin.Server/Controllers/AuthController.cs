using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Collections.Specialized;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Web;

namespace AngryMonkey.CloudLogin.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    IConfiguration configuration,
    CloudLoginServerConfiguration serverConfiguration,
    IHttpClientFactory httpClientFactory,
    ILogger<AuthController> logger,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<CloudLoginTokenClientOptions>? tokenOptions = null,
    ICloudLoginTokenProvider? tokenProvider = null) : ControllerBase
{
    /// <summary>
    /// Service-client settings for the token exchange. Null when this application has
    /// not registered as a service client, in which case sign-in still works but the
    /// application cannot prove the user's identity to downstream services.
    /// </summary>
    private readonly CloudLoginTokenClientOptions? _tokenOptions = tokenOptions?.Value;

    private readonly string _loginBaseUrl = serverConfiguration.LoginUrl ?? configuration["LoginUrl"] ?? throw new InvalidOperationException("LoginUrl configuration is missing.");
    private readonly TimeSpan _sessionDuration = serverConfiguration.SessionDuration;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<AuthController> _logger = logger;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("AngryMonkey.CloudLogin.Consumer.ReturnState.v1");

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        try
        {
            returnUrl = NormalizeLocalReturnUrl(returnUrl);

            string? callbackUrl = Url.Action("Callback", "Auth", new { state = EncodeReturnUrl(returnUrl) }, Request.Scheme);

            NameValueCollection queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["referer"] = callbackUrl;

            string finalUrl = $"{_loginBaseUrl}?{queryParams}";

            return Redirect(finalUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login redirect");
            return BadRequest("Login redirect failed");
        }
    }

    [HttpGet("profile")]
    public IActionResult Profile([FromQuery] string? returnUrl = null)
    {
        try
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return Login($"/auth/profile?returnUrl={HttpUtility.UrlEncode(returnUrl ?? "/")}");

            returnUrl = NormalizeLocalReturnUrl(returnUrl);

            string? callbackUrl = Url.Action("ProfileCallback", "Auth", new { state = EncodeReturnUrl(returnUrl) }, Request.Scheme);

            string profileUrl = $"{_loginBaseUrl}/Account";

            NameValueCollection queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["referer"] = callbackUrl;

            string finalUrl = $"{profileUrl}?{queryParams}";

            return Redirect(finalUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during profile redirect");
            return BadRequest("Profile redirect failed");
        }
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

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? requestId,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] bool keepMeSignedIn = false,
        [FromQuery] bool native = false)
    {
        try
        {
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Login callback received error: {Error}", error);
                return Redirect("/?error=login_failed");
            }

            if (!Guid.TryParse(requestId, out Guid parsedRequestId))
            {
                _logger.LogWarning("Login callback received without requestId");
                return Redirect("/?error=invalid_callback");
            }

            string returnUrl = NormalizeLocalReturnUrl(DecodeReturnUrl(state));

            // Prefer the token exchange: it consumes the request id and returns both the
            // user and the credentials this application needs to call other services on
            // their behalf. GetUserFromCloudLogin is the fallback for deployments that
            // have not registered a service client yet.
            CloudLoginTokenResponse? tokens = await ExchangeRequestForTokens(parsedRequestId);
            CloudUser? user = tokens?.User ?? (tokens is null
                ? await GetUserFromCloudLogin(parsedRequestId)
                : null);

            if (user == null)
            {
                _logger.LogWarning("Failed to retrieve user information for a login callback");
                return Redirect("/?error=user_not_found");
            }

            // Create authentication claims
            List<Claim> claims =
            [
                new(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new(ClaimTypes.Name, user.DisplayName ?? user.FirstName ?? "User"),
                new(ClaimTypes.Email, user.PrimaryEmailAddress?.Input ?? ""),
                new("CloudLoginRequestId", parsedRequestId.ToString())
            ];

            if (!string.IsNullOrEmpty(user.FirstName))
                claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

            if (!string.IsNullOrEmpty(user.LastName))
                claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

            // Include profile picture in claims if available (OIDC-compatible "picture" claim)
            if (!string.IsNullOrWhiteSpace(user.ProfilePicture))
                claims.Add(new Claim("picture", user.ProfilePicture));

            // Optionally include country/locale if needed later
            if (!string.IsNullOrWhiteSpace(user.Country))
                claims.Add(new Claim("country", user.Country));
            if (!string.IsNullOrWhiteSpace(user.Locale))
                claims.Add(new Claim("locale", user.Locale));

            // Create authentication identity and principal
            ClaimsIdentity claimsIdentity = new(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

            AuthenticationProperties properties = new()
            {
                IsPersistent = keepMeSignedIn,
                ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(_sessionDuration) : null
            };

            // Tokens ride inside the authentication cookie, which Data Protection
            // encrypts and the browser marks HttpOnly. They are therefore unreachable
            // from JavaScript, unlike tokens kept in local storage.
            if (tokens is not null)
                properties.StoreTokens(
                [
                    new AuthenticationToken
                    {
                        Name = CloudLoginTokenProvider.AccessTokenName,
                        Value = tokens.AccessToken
                    },
                    new AuthenticationToken
                    {
                        Name = CloudLoginTokenProvider.RefreshTokenName,
                        Value = tokens.RefreshToken ?? string.Empty
                    },
                    new AuthenticationToken
                    {
                        Name = CloudLoginTokenProvider.ExpiresOnName,
                        Value = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn).ToString("o")
                    }
                ]);

            // Sign in the user
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, properties);

            _logger.LogInformation("Login callback completed successfully");

            // Native clients cannot share the system browser's cookie jar. Return the
            // resolved user in the same one-time exchange that creates this API's cookie,
            // so the request id is never consumed twice.
            if (native)
                return Ok(user);

            // Append requestId to return URL so WASM client can fetch and persist user
            string finalReturnUrl = CloudLoginShared.AppendQueryParameter(returnUrl, "rid", parsedRequestId.ToString());

            return Redirect(finalReturnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login callback");
            return Redirect("/?error=callback_failed");
        }
    }

    /// <summary>
    /// Hands this application's own front end a short-lived access token for a
    /// downstream service.
    /// <para>
    /// A browser or native client cannot use the downstream service's session cookie:
    /// that cookie belongs to another origin and was never issued to this user here.
    /// This endpoint closes that gap without weakening anything &mdash; the session
    /// cookie stays HttpOnly, the refresh token and the service-client secret never
    /// leave the server, and what the client receives expires in minutes and is
    /// accepted by exactly one service.
    /// </para>
    /// </summary>
    [HttpGet("token")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Token(
        [FromQuery] string? audience = null,
        CancellationToken cancellationToken = default)
    {
        if (tokenProvider is null || _tokenOptions is null)
            return Problem(
                "This application is not configured to issue downstream tokens.",
                statusCode: StatusCodes.Status501NotImplemented);

        // Authenticate against the cookie explicitly rather than through the default
        // scheme. The default forwards a bearer token to the JWT handler, which would
        // let a service that already holds a token for this application mint further
        // tokens from it — turning one audience into every audience.
        AuthenticateResult session = await HttpContext.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (!session.Succeeded)
            return Unauthorized();

        // A token request is only ever made by this application's own front end. Another
        // site cannot read the response across origins, but it can cause the request to
        // be sent, so refuse it at the source instead of relying on the browser to
        // withhold the result.
        // Answered as an API, not with the cookie handler's access-denied redirect: a 302
        // to a sign-in page would reach the caller as an HTML body where it expected a
        // token, turning a clear refusal into a parse failure.
        if (!IsFirstPartyRequest())
        {
            _logger.LogWarning("Rejected a cross-site downstream token request.");
            return Problem("Token requests are only served to this application's own front end.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        string requested = string.IsNullOrWhiteSpace(audience) ? _tokenOptions.Audience : audience;

        string? accessToken = await tokenProvider.GetAccessTokenAsync(
            requested,
            forceRefresh: false,
            cancellationToken);

        // The authority decides which audiences this application may delegate to, so a
        // null here is its answer, not a local policy decision.
        if (string.IsNullOrWhiteSpace(accessToken))
            return Problem($"No token could be issued for audience '{requested}'.",
                statusCode: StatusCodes.Status403Forbidden);

        return Ok(new CloudLoginDownstreamTokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = RemainingLifetimeSeconds(accessToken),
            Audience = requested
        });
    }

    /// <summary>
    /// Whether the request came from this application itself rather than another site.
    /// <para>
    /// <c>same-site</c> is rejected along with <c>cross-site</c>: a sibling subdomain is
    /// a different application, and this endpoint hands out credentials.
    /// </para>
    /// </summary>
    private bool IsFirstPartyRequest()
    {
        // Browsers always send this; native clients send neither header, and "none" is a
        // user-initiated navigation.
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

    /// <summary>
    /// Seconds the token has left, read from its own <c>exp</c> claim.
    /// <para>
    /// Reading, not validating: the client needs a refresh hint, and the only party that
    /// decides whether this token is acceptable is the service that receives it.
    /// </para>
    /// </summary>
    private static int RemainingLifetimeSeconds(string accessToken)
    {
        try
        {
            double seconds = (new JsonWebToken(accessToken).ValidTo - DateTime.UtcNow).TotalSeconds;
            return seconds > 0 ? (int)seconds : 0;
        }
        catch (ArgumentException)
        {
            // Unreadable means the client should not cache it at all.
            return 0;
        }
    }

    [HttpPost("logout")]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout([FromQuery] string? returnUrl = null)
    {
        try
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            _logger.LogInformation("User logged out successfully");

            returnUrl = NormalizeLocalReturnUrl(returnUrl);

            // Build absolute return URL for the consumer website
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            string absoluteReturnUrl = $"{baseUrl}{returnUrl}";

            // Redirect to the standalone CloudLogin service logout to clear its session too,
            // otherwise the user remains signed in on the login service and cannot switch accounts.
            string cloudLoginLogoutUrl = CloudLoginShared.BuildLogoutUrl(_loginBaseUrl, absoluteReturnUrl);
            return Redirect(cloudLoginLogoutUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return Redirect("/");
        }
    }

    /// <summary>
    /// Exchanges the single-use login request id for this application's tokens.
    /// <para>
    /// When service-client credentials are configured, the exchange returns an access
    /// and refresh token as well as the user, and those tokens become what downstream
    /// calls present. Without credentials it falls back to resolving just the user,
    /// which still signs the browser in but leaves this application unable to prove
    /// the user's identity to any other service.
    /// </para>
    /// </summary>
    private async Task<CloudLoginTokenResponse?> ExchangeRequestForTokens(Guid requestId)
    {
        if (string.IsNullOrWhiteSpace(_tokenOptions?.ClientId) ||
            string.IsNullOrWhiteSpace(_tokenOptions.ClientSecret) ||
            string.IsNullOrWhiteSpace(_tokenOptions.Audience))
            return null;

        try
        {
            HttpClient httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_loginBaseUrl);

            using HttpRequestMessage request = new(
                HttpMethod.Post,
                $"/CloudLogin/Token/FromRequest?requestId={Uri.EscapeDataString(requestId.ToString())}&audience={Uri.EscapeDataString(_tokenOptions.Audience)}");

            string credentials = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{_tokenOptions.ClientId}:{_tokenOptions.ClientSecret}"));

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            using HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Token exchange with CloudLogin failed. Status: {Status}",
                    response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CloudLoginTokenResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging the login request for tokens");
            return null;
        }
    }

    private async Task<CloudUser?> GetUserFromCloudLogin(Guid requestId)
    {
        try
        {
            HttpClient httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_loginBaseUrl);

            // Call CloudLogin API to get user by request ID
            using HttpResponseMessage response = await httpClient.GetAsync($"/CloudLogin/Request/GetUserByRequestId?requestId={Uri.EscapeDataString(requestId.ToString())}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get user from CloudLogin. Status: {Status}", response.StatusCode);
                return null;
            }

            CloudUser? user = await response.Content.ReadFromJsonAsync<CloudUser>();
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user from CloudLogin");
            return null;
        }
    }

    private string EncodeReturnUrl(string returnUrl) => _stateProtector.Protect(returnUrl);

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

    private string NormalizeLocalReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
