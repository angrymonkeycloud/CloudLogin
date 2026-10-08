using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Controllers;

public sealed record CloudLoginSessionStatusRequest(string? SessionId);

[ApiController]
[Route("CloudLogin/Token")]
public sealed class TokenController(
    CloudLoginTokenService tokenService,
    CloudLoginSigningKeyManager keyManager,
    ICloudLogin server,
    IOptions<CloudLoginTokenOptions> options,
    ILogger<TokenController> logger,
    IClientDirectory clients,
    IClientRequestAuthenticator authenticator) : ControllerBase
{
    private readonly CloudLoginTokenService _tokens = tokenService;
    private readonly ICloudLogin _server = server;
    private readonly ILogger<TokenController> _logger = logger;

    [HttpPost("Session")]
    [Authorize]
    public async Task<IActionResult> FromSession([FromQuery] string audience, CancellationToken cancellationToken = default)
    {
        CloudUser? user = await _server.CurrentUser();

        if (user is null || user.Id == Guid.Empty || user.IsLocked)
            return Unauthorized();

        try
        {
            CloudLoginTokenResponse response = await _tokens.IssueAsync(
                user,
                audience,
                sessionId: User.FindFirst(CloudLoginClaims.SessionId)?.Value,
                clientIp: ClientIp(),
                userAgent: UserAgent(),
                cancellationToken: cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    [HttpPost("Code")]
    [AllowAnonymous]
    public async Task<IActionResult> Code([FromBody] CloudLoginCodeRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, request.ClientId, cancellationToken);

        if (authentication.Client is not { } client)
            return Unauthorized(new { error = "invalid_client" });

        // A public client's tokens are bound to its own origin, whatever it asks for; a backend may name the audience it needs.
        string audience = string.IsNullOrWhiteSpace(request.Audience) ? client.Audience : request.Audience;

        if (client.IsPublic && !string.Equals(audience, client.Audience, StringComparison.Ordinal))
            return BadRequest(new { error = "invalid_target", error_description = "A client without a secret key receives tokens for its own origin only." });

        if (!Guid.TryParse(request.Code, out Guid requestId) || requestId == Guid.Empty)
            return BadRequest(new { error = "invalid_grant" });

        ILoginRequestRepository? requests = HttpContext.RequestServices.GetService<ILoginRequestRepository>();

        if (requests is null)
            return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Authorization codes need the CloudLogin core storage.");

        LoginRequestDocument? stored = await requests.GetAsync(requestId.ToString(), cancellationToken);

        if (stored is null || stored.Kind != LoginRequestKinds.Login || DocumentExpiry.IsExpired(stored) || !string.Equals(stored.ClientId, client.ClientId, StringComparison.Ordinal))
            return BadRequest(new { error = "invalid_grant" });

        bool redirectMatches = string.Equals(stored.RedirectUri, request.RedirectUri, StringComparison.Ordinal);
        bool pkceMatches = !string.IsNullOrEmpty(stored.CodeChallenge) && Pkce.Verify(stored.CodeChallenge, request.CodeVerifier);

        if (!redirectMatches || !pkceMatches)
        {
            await _server.GetUserByRequestId(requestId);
            _logger.LogWarning("Authorization code for client {ClientId} was redeemed with a wrong redirect or verifier and has been burned.", client.ClientId);
            return BadRequest(new { error = "invalid_grant" });
        }

        CloudLoginRequestOrigin? origin = _server is CloudLoginServer cloudLoginServer ? await cloudLoginServer.GetLoginRequestOrigin(requestId) : null;
        CloudUser? user = await _server.GetUserByRequestId(requestId);

        if (user is null || user.Id == Guid.Empty || user.IsLocked)
            return BadRequest(new { error = "invalid_grant" });

        try
        {
            CloudLoginTokenResponse response = await _tokens.IssueAsync(
                user,
                audience,
                sessionId: origin?.SessionId,
                includeRefreshToken: client.ReceivesRefreshTokens,
                clientIp: origin?.IpAddress ?? ClientIp(),
                userAgent: origin?.UserAgent ?? UserAgent(),
                clientId: client.ClientId,
                cancellationToken: cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    [HttpPost("FromRequest")]
    [AllowAnonymous]
    public async Task<IActionResult> FromRequest([FromQuery] Guid requestId, [FromQuery] string audience, CancellationToken cancellationToken = default)
    {
        CloudLoginWebConfiguration? configuration = HttpContext.RequestServices.GetService<CloudLoginWebConfiguration>();

        if (configuration?.AllowLegacyRedirectHandoff != true)
            return BadRequest(new { error = "unsupported_grant_type", error_description = "Redeem a sign-in with the authorization code endpoint, with the PKCE verifier." });

        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, null, cancellationToken);

        if (authentication.Client is not { IsPublic: false } client)
            return Unauthorized(new { error = "invalid_client" });

        if (requestId == Guid.Empty)
            return BadRequest(new { error = "invalid_request" });

        ILoginRequestRepository? loginRequests = HttpContext.RequestServices.GetService<ILoginRequestRepository>();

        if (loginRequests is not null && await loginRequests.GetAsync(requestId.ToString(), cancellationToken) is { ClientId: { Length: > 0 } })
            return BadRequest(new { error = "invalid_grant", error_description = "This request is bound to a transaction and must be redeemed with its code verifier." });

        CloudLoginRequestOrigin? origin = _server is CloudLoginServer cloudLoginServer ? await cloudLoginServer.GetLoginRequestOrigin(requestId) : null;
        CloudUser? user = await _server.GetUserByRequestId(requestId);

        if (user is null || user.Id == Guid.Empty || user.IsLocked)
            return Unauthorized(new { error = "invalid_grant" });

        try
        {
            CloudLoginTokenResponse response = await _tokens.IssueAsync(
                user,
                audience,
                sessionId: origin?.SessionId,
                clientIp: origin?.IpAddress ?? ClientIp(),
                userAgent: origin?.UserAgent ?? UserAgent(),
                clientId: client.ClientId,
                cancellationToken: cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    [HttpPost("Refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] CloudLoginRefreshRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, request.ClientId, cancellationToken);

        CloudLoginTokenResponse? response = await _tokens.RefreshAsync(
            request.RefreshToken,
            async (userId, token) => await _server.GetUserById(userId),
            ClientIp(),
            UserAgent(),
            cancellationToken,
            authentication.Client);

        return response is null ? Unauthorized(new { error = "invalid_grant" }) : Ok(response);
    }

    [HttpPost("Exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange([FromBody] CloudLoginExchangeRequest request, CancellationToken cancellationToken = default)
    {
        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, null, cancellationToken);

        if (authentication.Client is not { } client)
            return Unauthorized(new { error = "invalid_client" });

        CloudLoginTokenResponse? response = await _tokens.ExchangeAsync(
            request.SubjectToken,
            request.Audience,
            client,
            async (userId, token) => await _server.GetUserById(userId),
            cancellationToken);

        return response is null ? Unauthorized(new { error = "invalid_grant" }) : Ok(response);
    }

    [HttpPost("Revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> Revoke([FromBody] CloudLoginRevokeRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, null, cancellationToken);
            await _tokens.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken, authentication.Client);
        }

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!await CallerOwnsSessionAsync(request.SessionId, cancellationToken))
                return Unauthorized(new { error = "invalid_grant" });

            await _tokens.RevokeSessionAsync(request.SessionId, cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("SessionStatus")]
    [AllowAnonymous]
    public async Task<IActionResult> SessionStatus([FromBody] CloudLoginSessionStatusRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, null, cancellationToken);

        if (authentication.Client is not { IsPublic: false })
            return Unauthorized(new { error = "invalid_client" });

        return Ok(new { active = !string.IsNullOrWhiteSpace(request.SessionId) && await _tokens.IsSessionActiveAsync(request.SessionId, cancellationToken) });
    }

    private async Task<bool> CallerOwnsSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        string authorization = Request.Headers.Authorization.ToString();

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            System.Security.Claims.ClaimsPrincipal? principal = await _tokens.ValidateAccessTokenAsync(authorization["Bearer ".Length..].Trim(), audience: null, cancellationToken);

            if (principal is null || !string.Equals(principal.FindFirst(CloudLoginClaims.SessionId)?.Value, sessionId, StringComparison.Ordinal))
                return false;

            return Guid.TryParse(principal.FindFirst(CloudLoginClaims.Subject)?.Value, out Guid subject) && await _tokens.OwnsSessionAsync(sessionId, subject, cancellationToken);
        }

        if (User.Identity?.IsAuthenticated != true)
            return false;

        CloudUser? user = await _server.CurrentUser();

        return user is { Id: var id, IsLocked: false } && id != Guid.Empty && await _tokens.OwnsSessionAsync(sessionId, id, cancellationToken);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent() => Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;
}

[ApiController]
public sealed class CloudLoginDiscoveryController(CloudLoginSigningKeyManager keyManager, IOptions<CloudLoginTokenOptions> options) : ControllerBase
{
    private readonly CloudLoginSigningKeyManager _keys = keyManager;
    private readonly CloudLoginTokenOptions _options = options.Value;

    [HttpGet("/.well-known/openid-configuration")]
    [AllowAnonymous]
    public IActionResult Discovery()
    {
        string issuer = _options.Issuer.TrimEnd('/');

        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(new
        {
            issuer,
            jwks_uri = $"{issuer}/.well-known/jwks.json",
            authorization_endpoint = $"{issuer}/CloudLogin/Authorize",
            token_endpoint = $"{issuer}/CloudLogin/Token/Code",
            refresh_endpoint = $"{issuer}/CloudLogin/Token/Refresh",
            revocation_endpoint = $"{issuer}/CloudLogin/Token/Revoke",
            token_exchange_endpoint = $"{issuer}/CloudLogin/Token/Exchange",
            authorization_transaction_endpoint = $"{issuer}/CloudLogin/Authorize/Begin",
            logout_transaction_endpoint = $"{issuer}/CloudLogin/Authorize/Logout",
            session_status_endpoint = $"{issuer}/CloudLogin/Token/SessionStatus",
            id_token_signing_alg_values_supported = new[] { "ES256" },
            response_types_supported = new[] { "code" },
            response_modes_supported = new[] { "query" },
            subject_types_supported = new[] { "public" },
            grant_types_supported = new[] { "authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:token-exchange" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "none" },
            backchannel_logout_supported = true,
            backchannel_logout_session_supported = true
        });
    }

    [HttpGet("/.well-known/jwks.json")]
    [AllowAnonymous]
    public async Task<IActionResult> Jwks(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300";

        return Ok(await _keys.GetJsonWebKeySetAsync(cancellationToken));
    }
}
