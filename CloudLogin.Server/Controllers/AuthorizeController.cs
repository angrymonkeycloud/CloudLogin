using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Server.Controllers;

/// <param name="BackChannelLogoutUri">
/// Where this backend wants to be told a session ended. It must be on the same origin as <paramref name="ReturnUrl"/>, so a key holder can
/// only ask to be notified at its own address.
/// </param>
public sealed record BeginAuthorizationRequest(string? ReturnUrl, string? State, string? CodeChallenge, string? CodeChallengeMethod, string? BackChannelLogoutUri = null);

public sealed record BeginLogoutRequest(string? PostLogoutRedirectUri, string? State, string? ClientId = null);

[ApiController]
[Route("CloudLogin/Authorize")]
public sealed class AuthorizeController(
    IClientRequestAuthenticator authenticator,
    IClientDirectory clients,
    AuthorizationTransactionService transactions,
    CloudLoginTokenService tokens,
    IOptions<CloudLoginTokenOptions> options) : ControllerBase
{
    /// <summary>A website with a backend opens a sign-in with its secret key, for wherever it runs. Nothing about it is registered.</summary>
    [HttpPost("Begin")]
    [AllowAnonymous]
    public async Task<IActionResult> Begin([FromBody] BeginAuthorizationRequest request, CancellationToken cancellationToken)
    {
        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, null, cancellationToken);

        if (authentication.Client is not { IsPublic: false } client)
            return Unauthorized(new { error = "invalid_client" });

        if (!string.IsNullOrWhiteSpace(request.BackChannelLogoutUri) && !SameOrigin(request.BackChannelLogoutUri, request.ReturnUrl))
            return BadRequest(new { error = "invalid_request", error_description = "The back-channel logout URL must be on the same origin as the return URL." });

        try
        {
            AuthorizationTransaction transaction = await transactions.BeginAsync(client, request.ReturnUrl, request.State, request.CodeChallenge, request.CodeChallengeMethod, cancellationToken);

            await clients.RecordBackendAsync(client.ClientId, ClientIds.Origin(new Uri(transaction.ReturnUrl)), string.IsNullOrWhiteSpace(request.BackChannelLogoutUri) ? null : request.BackChannelLogoutUri, cancellationToken);

            return TransactionResponse(transaction);
        }
        catch (AdminException exception) when (exception.Kind == AdminErrorKinds.Invalid)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    /// <summary>
    /// A website without a backend or a native app. Its identity is the origin of <paramref name="redirectUri"/>; <c>client_id</c> is not
    /// needed and not trusted. PKCE is required, and the tokens it ends up with are valid only for that origin.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        CancellationToken cancellationToken)
    {
        ClientAuthenticationResult authentication = await clients.AuthenticatePublicAsync(redirectUri ?? string.Empty, cancellationToken);

        if (authentication.Client is not { IsPublic: true } client)
            return BadRequest(new { error = authentication.Failure == ClientAuthenticationFailures.InvalidClientId ? "invalid_request" : "unauthorized_client" });

        try
        {
            AuthorizationTransaction transaction = await transactions.BeginAsync(client, redirectUri, state, codeChallenge, codeChallengeMethod, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return Redirect(LoginUrl(transaction));
        }
        catch (AdminException exception) when (exception.Kind == AdminErrorKinds.Invalid)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    [HttpPost("Logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] BeginLogoutRequest request, CancellationToken cancellationToken)
    {
        ClientRequestAuthentication authentication = await authenticator.AuthenticateAsync(Request, request.ClientId, cancellationToken);

        if (authentication.Client is not { } client || (client.IsPublic && !await HoldsLiveSessionAsync(client, cancellationToken)))
            return Unauthorized(new { error = "invalid_client" });

        try
        {
            AuthorizationTransaction transaction = await transactions.BeginLogoutAsync(client, request.PostLogoutRedirectUri, request.State, cancellationToken);
            Response.Headers.CacheControl = "no-store";

            return Ok(new
            {
                transaction = transaction.Reference,
                logoutUrl = $"{options.Value.Issuer.TrimEnd('/')}/CloudLogin/Logout?referer={Uri.EscapeDataString(transaction.Reference)}",
                expiresOn = transaction.ExpiresOn
            });
        }
        catch (AdminException exception) when (exception.Kind == AdminErrorKinds.Invalid)
        {
            return BadRequest(new { error = "invalid_request", error_description = exception.Message });
        }
    }

    /// <summary>A public client has no secret, so it proves it is acting for a real session by presenting a live access token issued to it.</summary>
    private async Task<bool> HoldsLiveSessionAsync(ClientIdentity client, CancellationToken cancellationToken)
    {
        string authorization = Request.Headers.Authorization.ToString();

        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        System.Security.Claims.ClaimsPrincipal? principal = await tokens.ValidateAccessTokenAsync(authorization["Bearer ".Length..].Trim(), client.Audience, cancellationToken);
        string? sessionId = principal?.FindFirst(CloudLoginClaims.SessionId)?.Value;

        return !string.IsNullOrEmpty(sessionId)
            && Guid.TryParse(principal!.FindFirst(CloudLoginClaims.Subject)?.Value, out Guid subject)
            && await tokens.OwnsSessionAsync(sessionId, subject, cancellationToken)
            && await tokens.IsSessionActiveAsync(sessionId, cancellationToken);
    }

    private static bool SameOrigin(string? first, string? second) =>
        ClientIds.IsBackendUrl(first, out Uri? a) && Uri.TryCreate(second, UriKind.Absolute, out Uri? b) && ClientIds.Origin(a!) == ClientIds.Origin(b);

    private string LoginUrl(AuthorizationTransaction transaction) => $"{options.Value.Issuer.TrimEnd('/')}/?referer={Uri.EscapeDataString(transaction.Reference)}";

    private IActionResult TransactionResponse(AuthorizationTransaction transaction)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { transaction = transaction.Reference, loginUrl = LoginUrl(transaction), expiresOn = transaction.ExpiresOn });
    }
}
