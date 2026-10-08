using System.Security.Cryptography;
using System.Text;
using AngryMonkey.CloudLogin.Server.Core.Abstractions;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public sealed record AuthorizationTransaction(
    string TransactionId,
    string ClientId,
    string ReturnUrl,
    string? State,
    DateTimeOffset ExpiresOn,
    string? CodeChallenge = null,
    LoginRequestKinds Kind = LoginRequestKinds.Authorization)
{
    public string Reference => AuthorizationTransactionService.ReferencePrefix + TransactionId;
}

public static class Pkce
{
    public const string Method = "S256";

    public static string CreateChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static bool IsValidVerifier(string? verifier) =>
        verifier is { Length: >= 43 and <= 128 } && verifier.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~');

    public static bool IsValidChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static bool Verify(string challenge, string verifier) =>
        IsValidVerifier(verifier) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CreateChallenge(verifier)), Encoding.ASCII.GetBytes(challenge));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class AuthorizationTransactionService(ILoginRequestRepository requests, IAuditLogger audit, TimeProvider? clock = null)
{
    public const string ReferencePrefix = "cltx:";
    public const int MinStateLength = 16;
    public const int MaxStateLength = 512;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public static bool TryParseReference(string? value, out string transactionId)
    {
        transactionId = string.Empty;

        if (value is null || !value.StartsWith(ReferencePrefix, StringComparison.Ordinal))
            return false;

        string candidate = value[ReferencePrefix.Length..];

        if (!Guid.TryParseExact(candidate, "N", out _))
            return false;

        transactionId = candidate;
        return true;
    }

    /// <summary>
    /// Opens a sign-in. A backend (it holds a secret key) may return anywhere on https; a public client returns only to its own origin,
    /// which is its identity, so the code and the tokens it leads to can only reach the site or app that asked.
    /// </summary>
    public async Task<AuthorizationTransaction> BeginAsync(ClientIdentity client, string? returnUrl, string? state, string? codeChallenge, string? codeChallengeMethod = null, CancellationToken cancellationToken = default)
    {
        if (!IsPermittedDestination(client, returnUrl, out string normalized))
            throw await RejectAsync(client, "InvalidReturnUrl", client.IsPublic
                ? "The return URL must be on the origin that identifies this application."
                : "The return URL must be an absolute https URL (http is allowed only for localhost).", cancellationToken);

        if (state is not { Length: >= MinStateLength and <= MaxStateLength })
            throw await RejectAsync(client, "InvalidState", $"A state of {MinStateLength} to {MaxStateLength} characters is required.", cancellationToken);

        if (!string.IsNullOrEmpty(codeChallengeMethod) && !string.Equals(codeChallengeMethod, Pkce.Method, StringComparison.Ordinal))
            throw await RejectAsync(client, "UnsupportedChallengeMethod", "Only the S256 code challenge method is supported.", cancellationToken);

        if (!Pkce.IsValidChallenge(codeChallenge))
            throw await RejectAsync(client, "InvalidCodeChallenge", "A PKCE S256 code challenge is required.", cancellationToken);

        return await CreateAsync(client, LoginRequestKinds.Authorization, normalized, state, codeChallenge, AuditEventTypes.TransactionStarted, cancellationToken);
    }

    public async Task<AuthorizationTransaction> BeginLogoutAsync(ClientIdentity client, string? postLogoutRedirectUri, string? state, CancellationToken cancellationToken = default)
    {
        if (!IsPermittedDestination(client, postLogoutRedirectUri, out string normalized))
            throw await RejectAsync(client, "InvalidPostLogoutUrl", client.IsPublic
                ? "The post-logout URL must be on the origin that identifies this application."
                : "The post-logout URL must be an absolute https URL (http is allowed only for localhost).", cancellationToken);

        if (state is { Length: > MaxStateLength })
            throw await RejectAsync(client, "StateTooLong", $"The state can be at most {MaxStateLength} characters.", cancellationToken);

        return await CreateAsync(client, LoginRequestKinds.Logout, normalized, state, null, AuditEventTypes.TransactionStarted, cancellationToken);
    }

    private static bool IsPermittedDestination(ClientIdentity client, string? url, out string normalized)
    {
        if (!ApplicationValidation.TryNormalizeUrl(url, allowCustomScheme: client.Kind == ApplicationKinds.NativeApp, out normalized))
            return false;

        return !client.IsPublic
            || (ClientIds.TryPublicIdentity(normalized, out string identity, out _) && string.Equals(identity, client.ClientId, StringComparison.Ordinal));
    }

    private async Task<AuthorizationTransaction> CreateAsync(ClientIdentity client, LoginRequestKinds kind, string url, string? state, string? challenge, string eventType, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string id = Guid.NewGuid().ToString("N");

        LoginRequestDocument document = new()
        {
            Id = id,
            Kind = kind,
            State = LoginRequestStates.Pending,
            ClientId = client.ClientId,
            ReturnUrl = url,
            ClientState = state,
            CodeChallenge = challenge,
            CreatedOn = now,
            ExpiresOn = now + Lifetime
        };

        DocumentExpiry.Recompute(document, now);
        await requests.CreateAsync(document, cancellationToken);

        await audit.LogAsync(new AuditEntry
        {
            EventType = eventType,
            ClientId = client.ClientId,
            Data = new Dictionary<string, string> { ["Kind"] = kind.ToString(), ["ReturnHost"] = new Uri(url).Authority }
        }, cancellationToken);

        return new AuthorizationTransaction(id, client.ClientId, url, state, document.ExpiresOn!.Value, challenge, kind);
    }

    public async Task<bool> IsPendingAsync(string transactionId, LoginRequestKinds kind = LoginRequestKinds.Authorization, CancellationToken cancellationToken = default) =>
        await FindOpenAsync(transactionId, kind, cancellationToken) is not null;

    public async Task<AuthorizationTransaction?> ConsumeAsync(string transactionId, LoginRequestKinds kind = LoginRequestKinds.Authorization, CancellationToken cancellationToken = default)
    {
        LoginRequestDocument? document = await FindOpenAsync(transactionId, kind, cancellationToken);

        if (document is null || !await requests.TryDeleteAsync(document, cancellationToken))
            return null;

        await audit.LogAsync(new AuditEntry { EventType = AuditEventTypes.TransactionCompleted, ClientId = document.ClientId, Data = new Dictionary<string, string> { ["Kind"] = kind.ToString() } }, cancellationToken);

        return new AuthorizationTransaction(document.Id, document.ClientId!, document.ReturnUrl!, document.ClientState, document.ExpiresOn!.Value, document.CodeChallenge, kind);
    }

    public async Task BindLoginRequestAsync(Guid requestId, AuthorizationTransaction transaction, CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            LoginRequestDocument? document = await requests.GetAsync(requestId.ToString(), cancellationToken);

            if (document is null || document.Kind != LoginRequestKinds.Login)
                break;

            document.ClientId = transaction.ClientId;
            document.RedirectUri = transaction.ReturnUrl;
            document.CodeChallenge = transaction.CodeChallenge;
            document.TransactionId = transaction.TransactionId;
            document.ClientState = transaction.State;

            if (await requests.TryReplaceAsync(document, cancellationToken))
                return;
        }

        throw new InvalidOperationException("The sign-in request could not be bound to its application.");
    }

    private async Task<LoginRequestDocument?> FindOpenAsync(string transactionId, LoginRequestKinds kind, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
            return null;

        LoginRequestDocument? document = await requests.GetAsync(transactionId, cancellationToken);

        return document is { State: LoginRequestStates.Pending }
            && document.Kind == kind
            && !string.IsNullOrEmpty(document.ClientId)
            && !string.IsNullOrEmpty(document.ReturnUrl)
            && document.ExpiresOn is not null
            && !DocumentExpiry.IsExpired(document, _clock.GetUtcNow())
                ? document
                : null;
    }

    private async Task<AdminException> RejectAsync(ClientIdentity client, string reason, string message, CancellationToken cancellationToken)
    {
        await audit.LogAsync(new AuditEntry
        {
            EventType = AuditEventTypes.TransactionRejected,
            ClientId = client.ClientId,
            Result = AuditResults.Denied,
            Data = new Dictionary<string, string> { ["Reason"] = reason }
        }, cancellationToken);

        return AdminException.Invalid(message);
    }
}
